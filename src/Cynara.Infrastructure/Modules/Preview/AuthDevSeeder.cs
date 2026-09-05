using Cynara.Application;
using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.Hospitals;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Hospitals;
using Cynara.Domain.Memberships;
using Cynara.Infrastructure.Modules.Identity;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

using OpenIddict.Abstractions;

namespace Cynara.Infrastructure.Modules.Preview;

/// <summary>
/// Development/preview authentication seed: provisions real users,
/// memberships, capability grants, and the confidential cynara-web
/// OpenIddict client whose fixed credential and loopback redirect URIs form
/// the demo contract. Idempotent, never runs outside Development.
/// </summary>
public static class AuthDevSeeder
{
#pragma warning disable S2068 // Encrypted credential: intentional demo seed value
#pragma warning disable S1075 // URIs should not be hardcoded: registered dev redirect URIs

    /// <summary>Seed demo user email.</summary>
    public const string DoctorEmail = "doctor@cynara.dev";

    /// <summary>Seed demo user password (Development-only credential).</summary>
    public const string DoctorPassword = "Cynara!Dev123";

    /// <summary>Actor identity inside the primary hospital.</summary>
    public const string PrimaryActor = "doctor-alpha";

    /// <summary>Actor identity inside the secondary hospital.</summary>
    public const string SecondaryActor = "doctor-beta";

    /// <summary>Business code of the secondary demo hospital.</summary>
    public const string SecondaryHospitalCode = "hosp-b";

    /// <summary>Confidential client id used by cynara-web.</summary>
    public const string WebClientId = "cynara-web";

    /// <summary>Confidential client secret (Development-only credential).</summary>
    public const string WebClientSecret = "cynara-web-secret";

    /// <summary>Authorization-code redirect URI (English locale).</summary>
    public const string WebRedirectUriEnglish = "http://localhost:5173/en/login";

    /// <summary>Authorization-code redirect URI (Spanish locale).</summary>
    public const string WebRedirectUriSpanish = "http://localhost:5173/es/login";

    /// <summary>Loopback authorization-code redirect URI (English locale).</summary>
    public const string WebRedirectUriLoopbackEnglish = "http://127.0.0.1:5173/en/login";

    /// <summary>Loopback authorization-code redirect URI (Spanish locale).</summary>
    public const string WebRedirectUriLoopbackSpanish = "http://127.0.0.1:5173/es/login";

    private static IReadOnlyList<string> WebRedirectUris =>
    [
        WebRedirectUriEnglish,
        WebRedirectUriSpanish,
        WebRedirectUriLoopbackEnglish,
        WebRedirectUriLoopbackSpanish,
    ];

#pragma warning restore S1075 // URIs should not be hardcoded: registered dev redirect URIs
#pragma warning restore S2068 // Encrypted credential: intentional demo seed value

    /// <summary>
    /// Seeds the bootstrap and secondary hospitals, the demo user with
    /// memberships, capability grants, and the web client registration.
    /// All operations are idempotent.
    /// </summary>
    public static async Task SeedAuthDevDataAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        AsyncServiceScope scope = services.CreateAsyncScope();
        try
        {
            IServiceProvider provider = scope.ServiceProvider;

            CynaraDbContext dbContext = provider.GetRequiredService<CynaraDbContext>();
            CynaraIdentityDbContext identity = provider
                .GetRequiredService<CynaraIdentityDbContext>();
            UserManager<CynaraUser> userManager = provider
                .GetRequiredService<UserManager<CynaraUser>>();

            Hospital primary = await EnsurePrimaryHospitalAsync(
                    dbContext,
                    provider,
                    cancellationToken)
                .ConfigureAwait(false);
            Hospital secondary = await EnsureSecondaryHospitalAsync(
                    dbContext,
                    cancellationToken)
                .ConfigureAwait(false);

            CynaraUser doctor = await EnsureDoctorUserAsync(
                    userManager)
                .ConfigureAwait(false);

            await EnsureMembershipAsync(
                    identity,
                    doctor,
                    primary,
                    PrimaryActor,
                    cancellationToken)
                .ConfigureAwait(false);
            await EnsureMembershipAsync(
                    identity,
                    doctor,
                    secondary,
                    SecondaryActor,
                    cancellationToken)
                .ConfigureAwait(false);

            await GrantCapabilitiesAsync(
                    provider,
                    primary,
                    PrimaryActor,
                    grantAll: true,
                    cancellationToken)
                .ConfigureAwait(false);
            await GrantCapabilitiesAsync(
                    provider,
                    secondary,
                    SecondaryActor,
                    grantAll: false,
                    cancellationToken)
                .ConfigureAwait(false);

            IOpenIddictApplicationManager applications = provider
                .GetRequiredService<IOpenIddictApplicationManager>();
            await EnsureWebClientAsync(
                    applications,
                    provider.GetRequiredService<IConfiguration>(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Merges the fixed localhost redirect URIs with one en/es pair derived
    /// from each configured <c>Preview:WebAppOrigins</c> entry.
    /// </summary>
    internal static IReadOnlyList<string> ResolveWebRedirectUris(
        IConfiguration? configuration)
    {
        string[]? origins = configuration?
            .GetSection("Preview:WebAppOrigins")
            .Get<string[]>();
        List<string> uris = [.. WebLoginRedirectUriBuilder.Build(origins)];
        uris.AddRange(WebRedirectUris);
        return uris;
    }

    private static async Task<Hospital> EnsurePrimaryHospitalAsync(
        CynaraDbContext dbContext,
        IServiceProvider provider,
        CancellationToken cancellationToken)
    {
        IConfiguration configuration = provider.GetRequiredService<IConfiguration>();
        string code = configuration["Hospitals:BootstrapCode"] ?? "default";
        Hospital? hospital = await dbContext.Hospitals
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Code == code, cancellationToken)
            .ConfigureAwait(false);
        if (hospital is not null)
        {
            return hospital;
        }

        hospital = NewHospital(code, "Espacio de trabajo predeterminado");
        _ = dbContext.Hospitals.Add(hospital);
        _ = await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return hospital;
    }

    private static async Task<Hospital> EnsureSecondaryHospitalAsync(
        CynaraDbContext dbContext,
        CancellationToken cancellationToken)
    {
        Hospital? hospital = await dbContext.Hospitals
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Code == SecondaryHospitalCode,
                cancellationToken)
            .ConfigureAwait(false);
        if (hospital is not null)
        {
            return hospital;
        }

        hospital = NewHospital(SecondaryHospitalCode, "Hospital Beta");
        _ = dbContext.Hospitals.Add(hospital);
        _ = await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return hospital;
    }

    private static Hospital NewHospital(string code, string name)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new Hospital
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            Status = HospitalStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static async Task<CynaraUser> EnsureDoctorUserAsync(
        UserManager<CynaraUser> userManager)
    {
        CynaraUser? existing = await userManager.FindByNameAsync(DoctorEmail)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        CynaraUser doctor = new()
        {
            UserName = DoctorEmail,
            Email = DoctorEmail,
            EmailConfirmed = true,
        };
        IdentityResult result = await userManager.CreateAsync(doctor, DoctorPassword)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Seed user creation failed: "
                + string.Join("; ", result.Errors.Select(error => error.Description)));
        }

        return doctor;
    }

    private static async Task EnsureMembershipAsync(
        CynaraIdentityDbContext identity,
        CynaraUser user,
        Hospital hospital,
        string actorId,
        CancellationToken cancellationToken)
    {
        bool exists = await identity.Memberships
            .AsNoTracking()
            .AnyAsync(
                item => item.UserId == user.Id
                    && item.HospitalId == hospital.Id
                    && item.Status == MembershipStatus.Active,
                cancellationToken)
            .ConfigureAwait(false);
        if (exists)
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        _ = identity.Memberships.Add(new Membership
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            HospitalId = hospital.Id,
            ActorId = actorId,
            CreatedAt = now,
            Status = MembershipStatus.Active,
            ActivatedAt = now,
            UpdatedAt = now,
        });
        _ = await identity.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Grants missing capabilities best-effort: a ConflictException from an
    /// already-held grant is swallowed so repeated seeds stay idempotent.
    /// </summary>
    private static async Task GrantCapabilitiesAsync(
        IServiceProvider provider,
        Hospital hospital,
        string actorId,
        bool grantAll,
        CancellationToken cancellationToken)
    {
        HospitalContext hospitalContext = provider
            .GetRequiredService<HospitalContext>();
        hospitalContext.SetWorkspace(hospital.Id, hospital.Code, hospital.Name);

        ICapabilityAssignmentService capabilities = provider
            .GetRequiredService<ICapabilityAssignmentService>();
        CapabilityAssignmentListResponse response = await capabilities
            .ListAsync(cancellationToken)
            .ConfigureAwait(false);
        var held = response.Items
            .Where(item => string.Equals(
                item.ActorId, actorId, StringComparison.Ordinal))
            .Select(item => item.Capability)
            .ToHashSet(StringComparer.Ordinal);

        IReadOnlyList<string> desired = grantAll
            ? [.. CapabilityCodes.All]
            : [CapabilityCodes.PatientsRead, CapabilityCodes.WorkspaceRead];
        foreach (string capability in desired)
        {
            if (held.Contains(capability))
            {
                continue;
            }

            try
            {
                _ = await capabilities
                    .GrantAsync(
                        new GrantCapabilityRequest(actorId, capability),
                        DoctorEmail,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ConflictException)
            {
                // Already held, ignore to keep idempotent
            }
        }
    }

    private static async Task EnsureWebClientAsync(
        IOpenIddictApplicationManager applications,
        IConfiguration? configuration,
        CancellationToken cancellationToken)
    {
        await OpenIddictWebClientRegistrar.EnsureAsync(
                applications,
                WebClientId,
                WebClientSecret,
                ResolveWebRedirectUris(configuration),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
