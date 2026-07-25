using System.Net;

using Cynara.Api.Tests.Support;
using Cynara.Domain.Failures;
using Cynara.Infrastructure.Persistence;

using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cynara.Api.Tests.Failures;

[Collection(PostgresFixtureDefinition.Name)]
public sealed class FailureLogTests : IDisposable
{
    public FailureLogTests(PostgreSqlDatabaseFixture database)
    {
        Factory = new FailureLogWebApplicationFactory(database.Settings);
        Client = Factory.CreateClient();
        Client.DefaultRequestHeaders.Add("X-Actor-Id", "failure-actor");
        Client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code", "default");
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
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
        CynaraDbContext dbContext = Factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateScope()
            .ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        return [.. dbContext.FailureLogs.AsNoTracking().ToList()];
    }
}

internal sealed class FailureLogWebApplicationFactory(TestDatabaseSettings database)
    : CynaraWebApplicationFactory(database)
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        _ = builder.ConfigureServices(services =>
        {
            _ = services.AddTransient<IStartupFilter, FailureTestEndpointsStartupFilter>();
        });
        return base.CreateHost(builder);
    }
}
