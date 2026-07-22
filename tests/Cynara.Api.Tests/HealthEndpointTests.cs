using Microsoft.AspNetCore.Mvc.Testing;

using Xunit;

namespace Cynara.Api.Tests;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        using var response = await _client.GetAsync(new Uri("/health", UriKind.Relative)).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
    }
}
