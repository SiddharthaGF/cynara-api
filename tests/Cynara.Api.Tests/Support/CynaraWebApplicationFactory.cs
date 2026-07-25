using Cynara.Api.Common.ActorContext;
using Cynara.Api.Hosting;
using Cynara.Application.Modules.Hospitals;
using Cynara.Infrastructure.Modules.Hospitals;
using Cynara.Infrastructure.Persistence;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Cynara.Api.Tests.Support;

internal class CynaraWebApplicationFactory : WebApplicationFactory<Program>
{
    public CynaraWebApplicationFactory(
        HospitalBootstrapOptions? bootstrapOptions = null)
    {
        BootstrapOptions = bootstrapOptions ?? BuildDefaultOptions();
        Database = InMemoryTestDatabaseFactory.Create();
    }

    public HospitalBootstrapOptions BootstrapOptions { get; }

    public InMemoryTestDatabaseFactory Database { get; }

    public CynaraDbContext CreateDbContext()
    {
        return Database.CreateDbContext();
    }

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
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.UseEnvironment("Development");

        _ = builder.ConfigureTestServices(services =>
        {
            // Replace the SqlServer-backed DbContext registration produced by
            // AddCynaraInfrastructure with an EF Core In-Memory store bound
            // to this factory's database name.
            _ = services.RemoveAll<DbContextOptions<CynaraDbContext>>();
            _ = services.RemoveAll<CynaraDbContext>();
            _ = services.AddSingleton(Database.ContextOptions);
            _ = services.AddDbContext<CynaraDbContext>((provider, options) =>
                _ = options.UseInMemoryDatabase(Database.DatabaseName)
                    .UseApplicationServiceProvider(provider)
                    .ConfigureWarnings(warnings =>
                        _ = warnings.Ignore(
                            InMemoryEventId.TransactionIgnoredWarning)));
            _ = services.PostConfigure<HospitalContextOptions>(options =>
            {
                options.HeaderName = BootstrapOptions.HeaderName
                    ?? HttpContextHospitalExtensions.DefaultHeaderName;
            });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.ConfigureHostConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Hospitals:BootstrapCode"] = BootstrapOptions.BootstrapCode
                        ?? HospitalBootstrap.DefaultBootstrapCode,
                    ["Hospitals:BootstrapName"] = BootstrapOptions.BootstrapName
                        ?? HospitalBootstrap.DefaultBootstrapName,
                    ["Hospitals:HeaderName"] = BootstrapOptions.HeaderName
                        ?? HttpContextHospitalExtensions.DefaultHeaderName,
                    ["Hospitals:AllowAutoBootstrap"] = BootstrapOptions.AllowAutoBootstrap
                        ? "true"
                        : "false",
                });
        });

        return base.CreateHost(builder);
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
    /// </summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync(
        string? hospitalCode = null,
        CancellationToken cancellationToken = default)
    {
        HttpClient client = CreateClient();
        client.AcceptJsonApi();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            string.IsNullOrWhiteSpace(hospitalCode)
                ? BootstrapOptions.BootstrapCode ?? "default"
                : hospitalCode);
        await EnsureBootstrapHospitalAsync(cancellationToken).ConfigureAwait(false);
        return client;
    }
}
