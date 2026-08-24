using Cynara.Api.Common.ActorContext;
using Cynara.Api.Hosting;
using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.Hospitals;
using Cynara.Infrastructure.Modules.Hospitals;
using Cynara.Infrastructure.Modules.Identity;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Cynara.Api.Tests.Support;

internal class CynaraWebApplicationFactory(
    TestDatabaseSettings database,
    CynaraWebApplicationFactoryOptions? options = null)
    : WebApplicationFactory<Program>
{
    private readonly SemaphoreSlim resetLock = new(1, 1);
    private bool resetPerformed;

    private CynaraWebApplicationFactoryOptions Options { get; } =
        options ?? new CynaraWebApplicationFactoryOptions();

    public HospitalBootstrapOptions BootstrapOptions { get; } =
        options?.BootstrapOptions ?? BuildDefaultOptions();

    private static HospitalBootstrapOptions BuildDefaultOptions()
    {
        return new HospitalBootstrapOptions
        {
            BootstrapCode = "default",
            BootstrapName = "Default workspace",
            HeaderName = "X-Hospital-Code",
            AllowAutoBootstrap = true,
        };
    }

    /// <summary>
    /// F1 seam (default): keeps the header-driven actor and marks every request
    /// authenticated so X-Actor-Id suites run without real tokens. Real-auth
    /// factories opt out and use genuine OpenIddict tokens end to end.
    /// </summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _ = builder.UseEnvironment(Options.EnvironmentName ?? "Development");
        _ = builder.UseCynaraTestDatabase(database);

        if (!Options.UseRealAuthentication)
        {
            _ = builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<
                    ICurrentActor,
                    CurrentActor>());
                services.AddSingleton<
                    IPostConfigureOptions<AuthenticationOptions>,
                    TestAuthenticationPostConfigure>();
                _ = services
                    .AddAuthentication(TestAuthenticationDefaults.Scheme)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationDefaults.Scheme,
                        configureOptions: null);
            });
        }

        if (Options.GrantAllCapabilities)
        {
            _ = builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<
                    IEffectiveCapabilityResolver,
                    GrantAllCapabilityResolver>());
                services.Replace(ServiceDescriptor.Scoped<
                    ICapabilityGuard>(_ => new FakeCapabilityGuard()));
            });
        }
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.ConfigureHostConfiguration(configuration =>
        {
            var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:Default"] = database.ConnectionString,
                ["Hospitals:BootstrapCode"] = BootstrapOptions.BootstrapCode
                    ?? HospitalBootstrap.DefaultBootstrapCode,
                ["Hospitals:BootstrapName"] = BootstrapOptions.BootstrapName
                    ?? HospitalBootstrap.DefaultBootstrapName,
                ["Hospitals:HeaderName"] = BootstrapOptions.HeaderName
                    ?? HttpContextHospitalExtensions.DefaultHeaderName,
                ["Hospitals:AllowAutoBootstrap"] = BootstrapOptions.AllowAutoBootstrap
                    ? "true"
                    : "false",
            };

            if (Options.EmulateRenderProxy)
            {
                settings["RENDER_SERVICE_TYPE"] = "web";
            }

            if (Options.OpenIddictCertificates is not null)
            {
                settings["OpenIddict:SigningCertificatePath"] =
                    Options.OpenIddictCertificates.SigningCertificatePath;
                settings["OpenIddict:SigningKeyPath"] =
                    Options.OpenIddictCertificates.SigningKeyPath;
                settings["OpenIddict:EncryptionCertificatePath"] =
                    Options.OpenIddictCertificates.EncryptionCertificatePath;
                settings["OpenIddict:EncryptionKeyPath"] =
                    Options.OpenIddictCertificates.EncryptionKeyPath;
            }

            foreach ((string key, string? value) in Options.ExtraConfiguration
                ?? new Dictionary<string, string?>(StringComparer.Ordinal))
            {
                settings[key] = value;
            }

            configuration.AddInMemoryCollection(settings);
        });

        _ = builder.ConfigureServices(services =>
        {
            _ = services.PostConfigure<HospitalContextOptions>(options =>
            {
                options.HeaderName = BootstrapOptions.HeaderName
                    ?? HttpContextHospitalExtensions.DefaultHeaderName;
            });
        });

        return base.CreateHost(builder);
    }

    public new HttpClient CreateClient()
    {
        EnsureReset().GetAwaiter().GetResult();
        return base.CreateClient();
    }

    public new HttpClient CreateClient(WebApplicationFactoryClientOptions options)
    {
        EnsureReset().GetAwaiter().GetResult();
        return base.CreateClient(options);
    }

    /// <summary>
    /// Truncates every table in the shared Postgres container (schema and
    /// sequences preserved), re-seeds the bootstrap hospital, and excludes
    /// both EF migration-history tables so applied stamps stay valid. The
    /// first <c>CreateClient</c> call triggers this; tests bypassing it can
    /// call directly (e.g. from <see cref="IAsyncLifetime.InitializeAsync"/>).
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        await resetLock.WaitAsync().ConfigureAwait(false);
        try
        {
            const string MigrationHistoryTable = "__EFMigrationsHistory";
            const string IdentityMigrationHistoryTable =
                CynaraIdentityDbContext.MigrationsHistoryTableName;
            const string TruncateSql = @"
DO $$
DECLARE
    tables_to_truncate TEXT;
BEGIN
    SELECT string_agg(format('%I.%I', schemaname, tablename), ', ' ORDER BY tablename)
        INTO tables_to_truncate
        FROM pg_tables
        WHERE schemaname = current_schema()
          AND tablename <> '" + MigrationHistoryTable + @"'
          AND tablename <> '" + IdentityMigrationHistoryTable + @"';

    IF tables_to_truncate IS NOT NULL THEN
        EXECUTE 'TRUNCATE TABLE ' || tables_to_truncate || ' RESTART IDENTITY CASCADE';
    END IF;
END $$;";

            await using AsyncServiceScope scope = Services.CreateAsyncScope();
            CynaraDbContext dbContext = scope.ServiceProvider
                .GetRequiredService<CynaraDbContext>();
            _ = await dbContext.Database
                .ExecuteSqlRawAsync(TruncateSql)
                .ConfigureAwait(false);
        }
        finally
        {
            _ = resetLock.Release();
        }

        await EnsureBootstrapHospitalAsync().ConfigureAwait(false);
        resetPerformed = true;
    }

    private async Task EnsureReset()
    {
        if (resetPerformed)
        {
            return;
        }

        await ResetDatabaseAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures the bootstrap hospital exists in the underlying database. Test
    /// suites should call this from their constructor or setup so requests
    /// can resolve tenant context.
    /// </summary>
    public async Task EnsureBootstrapHospitalAsync(
        CancellationToken cancellationToken = default)
    {
        await Services
            .EnsureBootstrapHospitalAsync(BootstrapOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Creates an <see cref="HttpClient"/> with the bootstrap hospital
    /// context and optional seam actor pre-applied, seeding the bootstrap
    /// hospital when needed. The header name is sourced from
    /// <see cref="HospitalBootstrapOptions.HeaderName"/> so test fixtures
    /// can override it via configuration.
    /// </summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync(
        string? actorId = null,
        string? hospitalCode = null,
        CancellationToken cancellationToken = default)
    {
        HttpClient client = CreateClient();
        client.AcceptJsonApi();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            BootstrapOptions.HeaderName ?? "X-Hospital-Code",
            string.IsNullOrWhiteSpace(hospitalCode)
                ? BootstrapOptions.BootstrapCode ?? "default"
                : hospitalCode);
        if (!string.IsNullOrWhiteSpace(actorId))
        {
            client.DefaultRequestHeaders.Add("X-Actor-Id", actorId);
        }

        await EnsureBootstrapHospitalAsync(cancellationToken).ConfigureAwait(false);
        return client;
    }
}
