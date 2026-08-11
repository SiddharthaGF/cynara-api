using Cynara.Domain.Capabilities;
using Cynara.Domain.Hospitals;
using Cynara.IdentitySpike.Domain;

using Microsoft.AspNetCore.Identity;

using OpenIddict.Abstractions;

namespace Cynara.IdentitySpike.Data;

/// <summary>
/// Deterministic seed for the disposable spike. Deletes and recreates the
/// database, then provisions two hospitals, one user with two memberships,
/// per-hospital capability assignments, and the OpenIddict confidential
/// client used by the demo flows.
/// </summary>
public static class SeedData
{
    /// <summary>Business code of the first demo hospital.</summary>
    public const string HospitalAlphaCode = "hosp-a";

    /// <summary>Business code of the second demo hospital.</summary>
    public const string HospitalBetaCode = "hosp-b";

    /// <summary>Seed user email.</summary>
    public const string DoctorEmail = "doctor@cynara.dev";

    /// <summary>Seed user password (dev-only credential).</summary>
    public const string DoctorPassword = "Cynara!Dev123";

    /// <summary>Actor identity inside Hospital Alpha.</summary>
    public const string ActorAlpha = "doctor-alpha";

    /// <summary>Actor identity inside Hospital Beta.</summary>
    public const string ActorBeta = "doctor-beta";

    /// <summary>Confidential client id used by the demo flows.</summary>
    public const string ClientId = "cynara-spike";

    /// <summary>Confidential client secret (dev-only credential).</summary>
    public const string ClientSecret = "spike-secret";

    /// <summary>
    /// Resets the spike database and provisions the demo data.
    /// </summary>
    public static async Task RunAsync(
        SpikeDbContext dbContext,
        UserManager<IdentityUser<Guid>> userManager,
        IOpenIddictApplicationManager applicationManager,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(applicationManager);

        _ = await dbContext.Database.EnsureDeletedAsync(cancellationToken)
            .ConfigureAwait(false);
        _ = await dbContext.Database.EnsureCreatedAsync(cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        Hospital alpha = new()
        {
            Code = HospitalAlphaCode,
            Name = "Hospital Alpha",
            Status = HospitalStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
        Hospital beta = new()
        {
            Code = HospitalBetaCode,
            Name = "Hospital Beta",
            Status = HospitalStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
        dbContext.Hospitals.AddRange(alpha, beta);

        IdentityUser<Guid> doctor = new()
        {
            UserName = DoctorEmail,
            Email = DoctorEmail,
            EmailConfirmed = true,
        };
        IdentityResult userResult = await userManager
            .CreateAsync(doctor, DoctorPassword)
            .ConfigureAwait(false);
        if (!userResult.Succeeded)
        {
            throw new InvalidOperationException(
                "Seed user creation failed: "
                + string.Join("; ", userResult.Errors.Select(e => e.Description)));
        }

        dbContext.Memberships.AddRange(
            new Membership
            {
                Id = Guid.NewGuid(),
                UserId = doctor.Id,
                HospitalId = alpha.Id,
                ActorId = ActorAlpha,
                CreatedAt = now,
            },
            new Membership
            {
                Id = Guid.NewGuid(),
                UserId = doctor.Id,
                HospitalId = beta.Id,
                ActorId = ActorBeta,
                CreatedAt = now,
            });

        dbContext.CapabilityAssignments.AddRange(
            Grant(alpha.Id, ActorAlpha, CapabilityCodes.PatientsRead, now),
            Grant(alpha.Id, ActorAlpha, CapabilityCodes.EncountersWrite, now),
            Grant(beta.Id, ActorBeta, CapabilityCodes.PatientsRead, now));

        _ = await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await SeedOpenIddictApplicationAsync(applicationManager, cancellationToken)
            .ConfigureAwait(false);
    }

    private static CapabilityAssignment Grant(
        Guid hospitalId,
        string actorId,
        string capability,
        DateTimeOffset now)
    {
        return new()
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalId,
            ActorId = actorId,
            Capability = capability,
            AssignedAt = now,
            AssignedBy = "spike-seed",
        };
    }

    private static async Task SeedOpenIddictApplicationAsync(
        IOpenIddictApplicationManager applicationManager,
        CancellationToken cancellationToken)
    {
        object? existing = await applicationManager
            .FindByClientIdAsync(ClientId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return;
        }

        _ = await applicationManager.CreateAsync(
            new OpenIddictApplicationDescriptor
            {
                ClientId = ClientId,
                ClientSecret = ClientSecret,
                ConsentType = OpenIddictConstants.ConsentTypes.Explicit,
                DisplayName = "Cynara Identity Spike client",
                ClientType = OpenIddictConstants.ClientTypes.Confidential,
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.Endpoints.Revocation,
                    OpenIddictConstants.Permissions.GrantTypes.Password,
                    OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                    OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                    OpenIddictConstants.Permissions.Scopes.Email,
                    OpenIddictConstants.Permissions.Scopes.Profile,
                    "scp:openid",
                    "scp:offline_access",
                    "scp:cynara_api",
                },
            },
            cancellationToken).ConfigureAwait(false);
    }
}
