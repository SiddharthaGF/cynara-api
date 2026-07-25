using System.Net;

using Cynara.Api.Tests.Support;
using Cynara.Domain.Failures;
using Cynara.Infrastructure.Persistence;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cynara.Api.Tests.Failures;

public sealed class FailureLogTests : IDisposable
{
    public FailureLogTests()
    {
        Factory = new FailureLogWebApplicationFactory();
        Client = Factory.CreateClient();
        Client.DefaultRequestHeaders.Add("X-Actor-Id", "failure-actor");
        Client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code", "default");
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task UnhandledException_PersistsFailureLogAndReturnsSanitizedResponse()
    {
        using HttpResponseMessage response = await Client.GetAsync(
            new Uri("/test/throw-unhandled", UriKind.Relative)).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.DoesNotContain("boom-unhandled", body, StringComparison.Ordinal);
        Assert.Contains("unexpected error", body, StringComparison.OrdinalIgnoreCase);

        FailureLog entry = Assert.Single(LoadFailureLogs());
        Assert.Equal(500, entry.StatusCode);
        Assert.Equal("GET", entry.RequestMethod);
        Assert.Equal("/test/throw-unhandled", entry.RequestPath);
        Assert.Equal("failure-actor", entry.ActorId);
        Assert.Contains("boom-unhandled", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CynaraException_DoesNotCreateFailureLog()
    {
        using HttpResponseMessage response = await Client.GetAsync(
            new Uri("/test/throw-validation", UriKind.Relative)).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        Assert.Empty(LoadFailureLogs());
    }

    private HttpClient Client { get; }

    private FailureLogWebApplicationFactory Factory { get; }

    private List<FailureLog> LoadFailureLogs()
    {
        using CynaraDbContext dbContext = Factory.Database.CreateDbContext();
        return [.. dbContext.FailureLogs.AsNoTracking().ToList()];
    }
}

internal sealed class FailureLogWebApplicationFactory : WebApplicationFactory<Program>
{
    public FailureLogWebApplicationFactory()
    {
        Database = InMemoryTestDatabaseFactory.Create();
    }

    public InMemoryTestDatabaseFactory Database { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddJsonFile(
                Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
                optional: false,
                reloadOnChange: false);
        })
            .ConfigureTestServices(services =>
        {
            _ = services.RemoveAll<DbContextOptions<CynaraDbContext>>();
            _ = services.RemoveAll<CynaraDbContext>();
            _ = services.AddSingleton(Database.ContextOptions);
            _ = services.AddDbContext<CynaraDbContext>((provider, options) =>
                _ = options.UseInMemoryDatabase(Database.DatabaseName)
                    .UseApplicationServiceProvider(provider)
                    .ConfigureWarnings(warnings =>
                        _ = warnings.Ignore(
                            InMemoryEventId.TransactionIgnoredWarning)));
            _ = services.AddTransient<IStartupFilter, FailureTestEndpointsStartupFilter>();
        });
    }
}
