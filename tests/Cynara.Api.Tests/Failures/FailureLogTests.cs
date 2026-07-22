using System.Net;

using Cynara.Domain.Failures;
using Cynara.Infrastructure.Persistence;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Xunit;

namespace Cynara.Api.Tests.Failures;

public sealed class FailureLogTests : IDisposable
{
    public FailureLogTests()
    {
        Factory = new FailureLogWebApplicationFactory();
        Client = Factory.CreateClient();
        Client.DefaultRequestHeaders.Add("X-Actor-Id", "failure-actor");
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
        DbContextOptions<CynaraDbContext> options = new DbContextOptionsBuilder<CynaraDbContext>()
            .UseSqlite(Factory.SharedConnection)
            .Options;

        using CynaraDbContext dbContext = new(options);
        return [.. dbContext.FailureLogs.AsNoTracking().ToList()];
    }
}

public sealed class FailureLogWebApplicationFactory : WebApplicationFactory<Program>
{
    public SqliteConnection SharedConnection { get; } = new("Data Source=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        SharedConnection.Open();

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddJsonFile(
                Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
                optional: false,
                reloadOnChange: false);
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CYNARA_ENABLE_TEST_ENDPOINTS"] = "true",
            });
        });

        builder.ConfigureServices(services =>
        {
            ServiceDescriptor? dbContextDescriptor = services.SingleOrDefault(
                descriptor => descriptor.ServiceType == typeof(DbContextOptions<CynaraDbContext>));
            if (dbContextDescriptor is not null)
            {
                _ = services.Remove(dbContextDescriptor);
            }

            _ = services.RemoveAll<CynaraDbContext>();

            _ = services.AddDbContext<CynaraDbContext>(options => options.UseSqlite(SharedConnection));
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SharedConnection.Dispose();
        }

        base.Dispose(disposing);
    }
}
