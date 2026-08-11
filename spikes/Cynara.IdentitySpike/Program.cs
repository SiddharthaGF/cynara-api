using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.Capabilities.Persistence;
using Cynara.Application.Modules.Hospitals;
using Cynara.IdentitySpike.Auth;
using Cynara.IdentitySpike.Data;
using Cynara.IdentitySpike.Endpoints;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(
    builder.Configuration["OpenIddict:Issuer"]
    ?? "http://localhost:5295");

builder.Services.AddDbContext<SpikeDbContext>(options =>
{
    _ = options.UseSqlite("Data Source=data/spike.db");
    _ = options.UseOpenIddict();
});

builder.Services
    .AddIdentity<IdentityUser<Guid>, IdentityRole<Guid>>()
    .AddEntityFrameworkStores<SpikeDbContext>()
    .AddDefaultTokenProviders();

builder.Services
    .AddOpenIddict()
    .AddCore(options =>
    {
        _ = options.UseEntityFrameworkCore()
            .UseDbContext<SpikeDbContext>();
    })
    .AddServer(options =>
    {
        _ = options
            .SetTokenEndpointUris("/connect/token")
            .SetRevocationEndpointUris("/connect/revocation");

        _ = options
            .AllowPasswordFlow()
            .AllowRefreshTokenFlow()
            .AllowClientCredentialsFlow();

        _ = options.RegisterScopes(
            OpenIddictConstants.Scopes.OpenId,
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Email,
            OpenIddictConstants.Scopes.OfflineAccess,
            "cynara_api");

        _ = options.RegisterAudiences("cynara-api");

        _ = options
            .SetAccessTokenLifetime(TimeSpan.FromMinutes(15))
            .SetRefreshTokenLifetime(TimeSpan.FromDays(7))
            .SetIdentityTokenLifetime(TimeSpan.FromMinutes(15));

        _ = options.AddDevelopmentSigningCertificate();
        _ = options.AddDevelopmentEncryptionCertificate();

        // Access tokens are plain JWTs so the issuer/audience/expiry claims
        // are directly inspectable. Production may keep encryption enabled.
        _ = options.DisableAccessTokenEncryption();

        _ = options.SetIssuer(
            builder.Configuration["OpenIddict:Issuer"]
            ?? "http://localhost:5295");

        _ = options.UseAspNetCore()
            .EnableTokenEndpointPassthrough()
            .DisableTransportSecurityRequirement();
    })
    .AddValidation(options =>
    {
        _ = options.AddAudiences("cynara-api");
        _ = options.UseLocalServer();
        _ = options.UseAspNetCore();
    });

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme =
            OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        options.DefaultScheme =
            OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme =
            OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
    });

builder.Services.AddControllers();

// Reused Cynara Application services, wired manually for the self-contained
// spike (the production host composition root is not referenced).
builder.Services.AddScoped<HospitalContext>();
builder.Services.AddScoped<IHospitalContext>(static services =>
    services.GetRequiredService<HospitalContext>());
builder.Services.AddScoped<ResolvedActor>();
builder.Services.AddScoped<ICurrentActor, PrincipalCurrentActor>();
builder.Services.AddScoped<ICapabilityAssignmentRepository,
    SpikeCapabilityAssignmentRepository>();
builder.Services.AddScoped<EffectiveCapabilityResolver>();
builder.Services.AddScoped<IEffectiveCapabilityResolver>(static services =>
    services.GetRequiredService<EffectiveCapabilityResolver>());
builder.Services.AddScoped<ICapabilityGuard, CapabilityGuard>();

WebApplication app = builder.Build();

// Disposable spike: reset and seed on every startup.
string dataDirectory = Path.Combine(
    app.Environment.ContentRootPath,
    "data");
Directory.CreateDirectory(dataDirectory);
await using AsyncServiceScope seedScope =
    app.Services.CreateAsyncScope();
{
    SpikeDbContext dbContext = seedScope.ServiceProvider
        .GetRequiredService<SpikeDbContext>();
    UserManager<IdentityUser<Guid>> userManager = seedScope.ServiceProvider
        .GetRequiredService<UserManager<IdentityUser<Guid>>>();
    IOpenIddictApplicationManager applicationManager = seedScope.ServiceProvider
        .GetRequiredService<IOpenIddictApplicationManager>();
    await SeedData.RunAsync(
        dbContext,
        userManager,
        applicationManager,
        CancellationToken.None).ConfigureAwait(false);
}

app.UseAuthentication();
app.UseMiddleware<MembershipResolutionMiddleware>();
app.UseAuthorization();

_ = app.MapControllers();
_ = app.MapMeEndpoints();
_ = app.MapProtectedEndpoints();

await app.RunAsync().ConfigureAwait(false);

internal sealed partial class Program
{
    private Program()
    {
    }
}
