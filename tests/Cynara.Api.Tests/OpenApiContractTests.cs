using System.Net;
using System.Text.Json;

using Cynara.Api.Tests.Support;

namespace Cynara.Api.Tests;

public sealed class OpenApiContractTests : IDisposable
{
    public OpenApiContractTests()
    {
        Client = Factory.CreateClient();
        Client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            Factory.BootstrapOptions.BootstrapCode ?? "default");
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task OpenApiDocument_IsAvailableAndValidJson()
    {
        using HttpResponseMessage response = await Client
            .GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative))
            .ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"OpenAPI document failed: {(int)response.StatusCode} {body}"));
        using var document = JsonDocument.Parse(body);

        Assert.True(document.RootElement.TryGetProperty("paths", out JsonElement paths));
        Assert.True(paths.TryGetProperty("/api/formDefinitions", out _));
        Assert.True(paths.TryGetProperty("/api/formVersions", out _));
        Assert.True(paths.TryGetProperty("/api/formResponses", out _));
        Assert.False(paths.TryGetProperty("/api/forms", out _));
        Assert.False(paths.TryGetProperty("/", out _));
        Assert.False(paths.TryGetProperty("/health", out _));

        HashSet<string> operationIds = [];
        foreach (JsonProperty path in paths.EnumerateObject())
        {
            foreach (JsonProperty operation in path.Value.EnumerateObject())
            {
                if (operation.Value.TryGetProperty("operationId", out JsonElement id))
                {
                    string value = id.GetString() ?? string.Empty;
                    Assert.True(
                        operationIds.Add(value),
                        $"Duplicate operationId: {value}");
                }
            }
        }

        string raw = document.RootElement.GetRawText();
        Assert.Contains("application/vnd.api+json", raw, StringComparison.Ordinal);
        Assert.Contains("filter", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("include", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("page", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sort", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("X-Actor-Id", raw, StringComparison.Ordinal);
        Assert.Contains("/api/formVersions/{id}/publish", raw, StringComparison.Ordinal);
        Assert.True(
            raw.Contains("errors", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("JsonApiError", StringComparison.Ordinal));

        Assert.True(document.RootElement.TryGetProperty("tags", out JsonElement tags));
        var tagNames = tags.EnumerateArray()
            .Select(static tag => tag.GetProperty("name").GetString() ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Form Definitions", tagNames);
        Assert.Contains("Form AI", tagNames);
        Assert.Contains("AI Provider Settings", tagNames);
        Assert.DoesNotContain("formDefinitions", tagNames);
        Assert.DoesNotContain("aiProviderSettings", tagNames);

        Assert.False(paths.TryGetProperty("/api/ai/settings", out _));
        Assert.True(paths.TryGetProperty("/api/ai/status", out _));
        Assert.True(
            paths.TryGetProperty(
                "/api/ai/forms/{formDefinitionId}/chat",
                out _));
        Assert.True(
            paths.TryGetProperty(
                "/api/ai/forms/{formDefinitionId}/chat/stream",
                out _));

        // JADNC docs distinguish collection (0 params) vs get-by-id (1 param).
        // X-Actor-Id must be injected after that filter or summaries break.
        Assert.True(
            paths.TryGetProperty("/api/aiProviderSettings", out JsonElement coll));
        Assert.True(coll.TryGetProperty("get", out JsonElement collGet));
        Assert.Contains(
            "collection",
            collGet.GetProperty("summary").GetString() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        Assert.True(
            paths.TryGetProperty(
                "/api/aiProviderSettings/{id}",
                out JsonElement byId));
        Assert.True(byId.TryGetProperty("get", out JsonElement byIdGet));
        Assert.Contains(
            "individual",
            byIdGet.GetProperty("summary").GetString() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(byId.TryGetProperty("head", out JsonElement byIdHead));
        Assert.False(
            string.IsNullOrWhiteSpace(byIdHead.GetProperty("summary").GetString()));
        Assert.True(byId.TryGetProperty("patch", out _));
    }

    [Fact]
    public async Task ScalarUi_IsAvailable()
    {
        using HttpResponseMessage response = await Client
            .GetAsync(new Uri("/scalar/v1", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task JsonApi_ListFormDefinitions_UsesJsonApiMediaType()
    {
        Client.AcceptJsonApi();
        using HttpResponseMessage response = await Client
            .GetAsync(new Uri("/api/formDefinitions", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            JsonApiMedia.ContentType,
            response.Content.Headers.ContentType?.MediaType ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    private HttpClient Client { get; }

    private CynaraWebApplicationFactory Factory { get; } = new();
}
