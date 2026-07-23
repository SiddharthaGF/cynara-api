using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;

using Xunit;

namespace Cynara.Api.Tests;

public sealed class HealthEndpointTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory = new();
    private readonly HttpClient client;

    public HealthEndpointTests()
    {
        client = factory.CreateClient();
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative)).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
