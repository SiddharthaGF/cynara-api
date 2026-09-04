using System.Net;
using System.Text.Json;

namespace Cynara.Api.Tests;

[Collection(PostgresFixtureDefinition.Name)]
public sealed class SchemaEndpointTests : IDisposable
{
    private readonly CynaraWebApplicationFactory factory;
    private readonly HttpClient client;

    public SchemaEndpointTests(PostgreSqlDatabaseFixture database)
    {
        factory = new CynaraWebApplicationFactory(database.Settings);
        client = factory.CreateClient();
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string, string> Contracts => new()
    {
        { "clinical-schema", "https://cynara.dev/schemas/v1/clinical-schema.schema.json" },
        { "ui-schema", "https://cynara.dev/schemas/v1/ui-schema.schema.json" },
        { "rules-schema", "https://cynara.dev/schemas/v1/rules-schema.schema.json" },
        { "workflow-schema", "https://cynara.dev/schemas/v1/workflow-schema.schema.json" },
    };

    public static TheoryData<string> ContractNames =>
        new(
            "clinical-schema",
            "ui-schema",
            "rules-schema",
            "workflow-schema",
            "profile-snapshot");

    [Theory]
    [MemberData(nameof(Contracts))]
    public async Task GetContract_ServesJsonSchema(string contract, string expectedId)
    {
        using var response = await client
            .GetAsync(new Uri($"/schemas/v1/{contract}.schema.json", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "application/schema+json",
            response.Content.Headers.ContentType?.MediaType);

        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(
            expectedId,
            document.RootElement.GetProperty("$id").GetString());
        Assert.True(
            document.RootElement
                .GetProperty("properties")
                .TryGetProperty("schemaVersion", out _),
            "A served contract must declare its versioned schema.");
    }

    /// <summary>
    /// The invitation profile snapshot is a data contract (not a form
    /// meta-schema), so it is asserted against its own canonical shape: the
    /// declared Draft 2020-12 dialect, canonical id, and the required
    /// <c>actorId</c> + <c>capabilities</c> keys.
    /// </summary>
    [Fact]
    public async Task GetProfileSnapshotContract_ServesDraft202012()
    {
        using var response = await client
            .GetAsync(new Uri(
                "/schemas/v1/profile-snapshot.schema.json",
                UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "application/schema+json",
            response.Content.Headers.ContentType?.MediaType);

        string body = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(
            "https://json-schema.org/draft/2020-12/schema",
            document.RootElement.GetProperty("$schema").GetString());
        Assert.Equal(
            "https://cynara.dev/schemas/v1/profile-snapshot.schema.json",
            document.RootElement.GetProperty("$id").GetString());

        JsonElement required = document.RootElement.GetProperty("required");
        IReadOnlyList<string> requiredKeys =
            [.. required.EnumerateArray().Select(item => item.GetString()!)];
        Assert.Contains("actorId", requiredKeys);
        Assert.Contains("capabilities", requiredKeys);
    }

    [Theory]
    [MemberData(nameof(ContractNames))]
    public async Task GetContract_SetsEtagAndCacheControl(string contract)
    {
        using var response = await client
            .GetAsync(new Uri($"/schemas/v1/{contract}.schema.json", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(
            string.IsNullOrEmpty(response.Headers.ETag?.Tag),
            "Schema responses must carry an ETag for revalidation.");
        Assert.Contains(
            "public",
            response.Headers.CacheControl?.ToString()
                ?? string.Empty,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Schema documents are public contract artifacts; they must not require
    /// the X-Hospital-Code tenant header.
    /// </summary>
    [Fact]
    public async Task GetContract_WorksWithoutHospitalContext()
    {
        using var response = await client
            .GetAsync(new Uri("/schemas/v1/workflow-schema.schema.json", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetUnknownContract_ReturnsNotFound()
    {
        using var response = await client
            .GetAsync(new Uri("/schemas/v1/unknown-schema.schema.json", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
