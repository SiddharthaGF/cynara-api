using System.Net;
using System.Text.Json;

using Cynara.Api.Tests.Support;

namespace Cynara.Api.Tests;

public sealed class HealthEndpointTests : IDisposable
{
    private readonly CynaraWebApplicationFactory factory = new();
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
        using HttpResponseMessage response = await client
            .GetAsync(new Uri("/health", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        JsonElement root = document.RootElement;

        Assert.Equal("cynara-api", root.GetProperty("service").GetString());
        Assert.Equal("ok", root.GetProperty("status").GetString());

        JsonElement.ArrayEnumerator probes = root.GetProperty("probes").EnumerateArray();
        HashSet<string> probeNames = [];
        while (probes.MoveNext())
        {
            JsonElement probe = probes.Current;
            probeNames.Add(probe.GetProperty("name").GetString()!);
            Assert.Equal(
                "ok",
                probe.GetProperty("status").GetString(),
                ignoreCase: true);
        }

        Assert.Contains("database", probeNames, StringComparer.Ordinal);
        Assert.Contains("schemas", probeNames, StringComparer.Ordinal);
    }
}
