using Cynara.Api.Common.ActorContext;
using Cynara.Api.Hosting;
using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.Hospitals;
using Cynara.Infrastructure.Modules.Hospitals;
using Cynara.Infrastructure.Persistence;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Cynara.Api.Tests.Support;

internal class CynaraWebApplicationFactory(
    TestDatabaseSettings database,
    HospitalBootstrapOptions? bootstrapOptions = null,
    bool emulateRenderProxy = false,
    bool grantAllCapabilities = true)
    : WebApplicationFactory<Program>
{
    private readonly SemaphoreSlim resetLock = new(1, 1);
    private bool resetPerformed;

    public HospitalBootstrapOptions BootstrapOptions { get; } =
        bootstrapOptions ?? BuildDefaultOptions();

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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _ = builder.UseEnvironment("Development");
        _ = builder.UseCynaraTestDatabase(database);

        if (grantAllCapabilities)
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

            if (emulateRenderProxy)
            {
                settings["RENDER_SERVICE_TYPE"] = "web";
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
    /// Truncates every table in the shared Postgres container so each test
    /// class starts from a clean slate. Schema, indexes, and sequences are
    /// preserved so other tests sharing the connection string keep working.
    /// The bootstrap hospital is re-seeded afterwards. The first
    /// <c>CreateClient</c> call on a given factory instance triggers this
    /// automatically; tests that bypass <c>CreateClient</c> can call it
    /// directly (e.g. from <see cref="IAsyncLifetime.InitializeAsync"/>).
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        await resetLock.WaitAsync().ConfigureAwait(false);
        try
        {
            const string TruncateSql = @"
DO $$
DECLARE
    tables_to_truncate TEXT;
BEGIN
    SELECT string_agg(format('%I.%I', schemaname, tablename), ', ' ORDER BY tablename)
        INTO tables_to_truncate
        FROM pg_tables
        WHERE schemaname = current_schema();

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
    /// context pre-applied and the bootstrap hospital already seeded.
    /// The header name is sourced from
    /// <see cref="HospitalBootstrapOptions.HeaderName"/> so test fixtures
    /// can override it via configuration.
    /// </summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync(
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
        await EnsureBootstrapHospitalAsync(cancellationToken).ConfigureAwait(false);
        return client;
    }
}
