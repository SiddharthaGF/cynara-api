using System.Net;
using System.Text.Json;

using Cynara.Api.Tests.Support;

namespace Cynara.Api.Tests;

[Collection(PostgresFixtureDefinition.Name)]
public sealed class OpenApiContractTests : IDisposable
{
    public OpenApiContractTests(PostgreSqlDatabaseFixture database)
    {
        DatabaseSettings = database.Settings;
        Factory = new CynaraWebApplicationFactory(DatabaseSettings);
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

        Assert.True(
            paths.TryGetProperty("/api/me/capabilities", out JsonElement myCaps));
        Assert.True(myCaps.TryGetProperty("get", out _));

        Assert.True(
            paths.TryGetProperty("/api/capabilities", out JsonElement caps));
        Assert.True(caps.TryGetProperty("get", out _));
        Assert.True(caps.TryGetProperty("post", out _));
        Assert.True(
            paths.TryGetProperty(
                "/api/capabilities/{actorId}/{capability}",
                out JsonElement capsByKey));
        Assert.True(capsByKey.TryGetProperty("delete", out _));

        Assert.Contains("Capabilities", tagNames);
    }

    [Fact]
    public async Task OpenApiDocument_Stage2Inventory_IsComplete()
    {
        using HttpResponseMessage response = await Client
            .GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative))
            .ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        JsonElement paths = document.RootElement.GetProperty("paths");

        var missing = new List<string>();
        var noResponses = new List<string>();
        var noSuccess = new List<string>();
        var unexpectedMediaTypes = new List<string>();
        var duplicateOperationIds = new List<string>();
        var operationIds = new HashSet<string>(StringComparer.Ordinal);

        foreach ((string path, string method) in Stage2Endpoints)
        {
            if (!paths.TryGetProperty(path, out JsonElement pathItem)
                || !pathItem.TryGetProperty(method, out JsonElement operation))
            {
                missing.Add($"{method.ToUpperInvariant()} {path}");
                continue;
            }

            if (operation.TryGetProperty("operationId", out JsonElement operationId))
            {
                string id = operationId.GetString() ?? string.Empty;
                if (!operationIds.Add(id))
                {
                    duplicateOperationIds.Add(id);
                }
            }

            if (!operation.TryGetProperty("responses", out JsonElement responses))
            {
                noResponses.Add($"{method.ToUpperInvariant()} {path}");
                continue;
            }

            if (!responses.EnumerateObject().Any(item =>
                    item.Name is "200" or "201" or "204"))
            {
                noSuccess.Add($"{method.ToUpperInvariant()} {path}");
            }

            foreach (JsonProperty item in responses.EnumerateObject())
            {
                if (!item.Value.TryGetProperty("content", out JsonElement content))
                {
                    continue;
                }

                foreach (JsonProperty mediaType in content.EnumerateObject())
                {
                    if (!IsAllowedMediaType(mediaType.Name))
                    {
                        unexpectedMediaTypes.Add(
                            $"{method.ToUpperInvariant()} {path}: {mediaType.Name}");
                    }
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            $"Missing Stage 2 endpoints: {string.Join(", ", missing)}");
        Assert.True(
            noResponses.Count == 0,
            $"Endpoints without documented responses: {string.Join(", ", noResponses)}");
        Assert.True(
            noSuccess.Count == 0,
            $"Endpoints without a 2xx response: {string.Join(", ", noSuccess)}");
        Assert.True(
            unexpectedMediaTypes.Count == 0,
            $"Unexpected response media types: {string.Join(", ", unexpectedMediaTypes)}");
        Assert.True(
            duplicateOperationIds.Count == 0,
            $"Duplicate operationIds: {string.Join(", ", duplicateOperationIds)}");
    }

    [Fact]
    public async Task OpenApiDocument_UsesForwardedHttpsSchemeOnRender()
    {
        await using var renderFactory = new CynaraWebApplicationFactory(
            DatabaseSettings,
            emulateRenderProxy: true);
        using HttpClient renderClient = renderFactory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri("/swagger/v1/swagger.json", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.10");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");

        using HttpResponseMessage response = await renderClient
            .SendAsync(request)
            .ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);

        JsonElement server = Assert.Single(
            document.RootElement.GetProperty("servers").EnumerateArray());
        string url = server.GetProperty("url").GetString() ?? string.Empty;
        Assert.StartsWith("https://", url, StringComparison.Ordinal);
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

    private TestDatabaseSettings DatabaseSettings { get; }

    private CynaraWebApplicationFactory Factory { get; }

    /// <summary>
    /// The full Stage 2 route map. Every entry must exist in the OpenAPI
    /// document with documented responses and only the supported media types
    /// (JSON:API, plain JSON, or SSE).
    /// </summary>
    private static readonly (string Path, string Method)[] Stage2Endpoints =
    [
        ("/api/formDefinitions", "get"),
        ("/api/formDefinitions", "post"),
        ("/api/formDefinitions/{id}", "get"),
        ("/api/formDefinitions/{id}", "patch"),
        ("/api/formDefinitions/{id}", "delete"),
        ("/api/formDefinitions/{id}/create-draft", "post"),
        ("/api/formDefinitions/{id}/soft-delete-draft", "delete"),
        ("/api/formVersions", "get"),
        ("/api/formVersions", "post"),
        ("/api/formVersions/{id}", "get"),
        ("/api/formVersions/{id}", "patch"),
        ("/api/formVersions/{id}", "delete"),
        ("/api/formVersions/{id}/submit-review", "post"),
        ("/api/formVersions/{id}/withdraw-review", "post"),
        ("/api/formVersions/{id}/reject-review", "post"),
        ("/api/formVersions/{id}/publish", "post"),
        ("/api/formVersions/{id}/retire", "post"),
        ("/api/formResponses", "get"),
        ("/api/formResponses", "post"),
        ("/api/formResponses/{id}", "get"),
        ("/api/formResponses/{id}", "patch"),
        ("/api/formResponses/{id}", "delete"),
        ("/api/formResponses/{id}/complete", "post"),
        ("/api/formResponseRevisions", "get"),
        ("/api/formResponseRevisions", "post"),
        ("/api/formResponseRevisions/{id}", "get"),
        ("/api/auditEvents", "get"),
        ("/api/auditEvents", "post"),
        ("/api/auditEvents/{id}", "get"),
        ("/api/auditEvents/{id}", "patch"),
        ("/api/auditEvents/{id}", "delete"),
        ("/api/aiProviderSettings", "get"),
        ("/api/aiProviderSettings", "post"),
        ("/api/aiProviderSettings/{id}", "get"),
        ("/api/aiProviderSettings/{id}", "patch"),
        ("/api/aiProviderSettings/{id}", "delete"),
        ("/api/documentDefinitions", "get"),
        ("/api/documentDefinitions", "post"),
        ("/api/documentDefinitions/{id}", "get"),
        ("/api/documentDefinitions/{id}", "patch"),
        ("/api/documentDefinitions/{id}", "delete"),
        ("/api/documentDefinitions/{id}/retire", "post"),
        ("/api/componentDefinitions", "get"),
        ("/api/componentDefinitions", "post"),
        ("/api/componentDefinitions/{id}", "get"),
        ("/api/componentDefinitions/{id}", "patch"),
        ("/api/componentDefinitions/{id}", "delete"),
        ("/api/componentDefinitions/{id}/create-draft", "post"),
        ("/api/componentDefinitions/{id}/soft-delete-draft", "delete"),
        ("/api/componentVersions", "get"),
        ("/api/componentVersions", "post"),
        ("/api/componentVersions/{id}", "get"),
        ("/api/componentVersions/{id}", "patch"),
        ("/api/componentVersions/{id}", "delete"),
        ("/api/componentVersions/{id}/publish", "post"),
        ("/api/componentVersions/{id}/retire", "post"),
        ("/api/workflowDefinitions", "get"),
        ("/api/workflowDefinitions", "post"),
        ("/api/workflowDefinitions/{id}", "get"),
        ("/api/workflowDefinitions/{id}", "patch"),
        ("/api/workflowDefinitions/{id}", "delete"),
        ("/api/workflowDefinitions/{id}/create-draft", "post"),
        ("/api/workflowDefinitions/{id}/soft-delete-draft", "delete"),
        ("/api/workflowVersions", "get"),
        ("/api/workflowVersions", "post"),
        ("/api/workflowVersions/{id}", "get"),
        ("/api/workflowVersions/{id}", "patch"),
        ("/api/workflowVersions/{id}", "delete"),
        ("/api/workflowVersions/{id}/submit-review", "post"),
        ("/api/workflowVersions/{id}/withdraw-review", "post"),
        ("/api/workflowVersions/{id}/reject-review", "post"),
        ("/api/workflowVersions/{id}/publish", "post"),
        ("/api/workflowVersions/{id}/retire", "post"),
        ("/api/patients", "get"),
        ("/api/patients", "post"),
        ("/api/patients/{id}", "get"),
        ("/api/patients/{id}", "patch"),
        ("/api/patients/{id}/soft-delete", "post"),
        ("/api/encounters", "get"),
        ("/api/encounters", "post"),
        ("/api/encounters/{id}", "get"),
        ("/api/encounters/{id}/complete", "post"),
        ("/api/encounters/{id}/cancel", "post"),
        ("/api/encounters/{id}/enter-in-error", "post"),
        ("/api/clinicalDocuments", "get"),
        ("/api/clinicalDocuments", "post"),
        ("/api/clinicalDocuments/{id}", "get"),
        ("/api/clinicalDocuments/{id}/complete", "post"),
        ("/api/clinicalDocuments/{id}/cancel", "post"),
        ("/api/clinicalDocuments/{id}/enter-in-error", "post"),
        ("/api/facilities", "get"),
        ("/api/facilities", "post"),
        ("/api/facilities/{id}", "patch"),
        ("/api/facilities/{id}/retire", "post"),
        ("/api/clinicalAreas", "get"),
        ("/api/clinicalAreas", "post"),
        ("/api/clinicalAreas/{id}", "patch"),
        ("/api/clinicalAreas/{id}/retire", "post"),
        ("/api/disciplines", "get"),
        ("/api/disciplines", "post"),
        ("/api/disciplines/{id}", "patch"),
        ("/api/disciplines/{id}/retire", "post"),
        ("/api/workspace", "get"),
        ("/api/workspace", "patch"),
        ("/api/capabilities", "get"),
        ("/api/capabilities", "post"),
        ("/api/capabilities/{actorId}/{capability}", "delete"),
        ("/api/me/capabilities", "get"),
        ("/api/ai/status", "get"),
        ("/api/ai/forms/{formDefinitionId}/chat", "post"),
        ("/api/ai/forms/{formDefinitionId}/chat/stream", "post"),
    ];

    private static bool IsAllowedMediaType(string mediaType)
    {
        return mediaType.StartsWith(
                "application/vnd.api+json",
                StringComparison.Ordinal)
            || mediaType.StartsWith(
                "application/json",
                StringComparison.Ordinal)
            || mediaType.StartsWith(
                "text/event-stream",
                StringComparison.Ordinal);
    }
}
