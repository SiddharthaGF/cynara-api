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

    /// <summary>
    /// JADNC docs distinguish collection (0 params) vs get-by-id (1 param);
    /// the security requirement is injected after that filter so summaries
    /// keep their collection/individual distinction.
    /// </summary>
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
        Assert.DoesNotContain("X-Actor-Id", raw, StringComparison.Ordinal);
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
        Assert.Contains("Pipelines", tagNames);
        Assert.Contains("Tasks", tagNames);
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

        Assert.True(paths.TryGetProperty("/api/aiProviderSettings", out JsonElement coll));
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
            paths.TryGetProperty("/api/pipelines", out JsonElement pipelines));
        Assert.True(pipelines.TryGetProperty("get", out _));
        Assert.True(pipelines.TryGetProperty("post", out _));
        Assert.True(
            paths.TryGetProperty(
                "/api/pipelines/{id}/advance",
                out JsonElement advance));
        Assert.True(advance.TryGetProperty("post", out _));
        Assert.True(
            paths.TryGetProperty("/api/tasks", out JsonElement tasks));
        Assert.True(tasks.TryGetProperty("get", out _));
        Assert.True(
            paths.TryGetProperty(
                "/api/tasks/{id}/claim",
                out JsonElement claim));
        Assert.True(claim.TryGetProperty("post", out _));

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

    /// <summary>
    /// Bearer replaces the legacy X-Actor-Id scheme; OAuth2 mirrors the
    /// /connect flows. Protected operations require Bearer + HospitalCode
    /// together, while the tenant-exempt listing stays bearer-only so clients
    /// can enumerate hospitals before choosing one.
    /// </summary>
    [Fact]
    public async Task OpenApiDocument_DescribesBearerAndOAuth2Security()
    {
        using HttpResponseMessage response = await Client
            .GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative))
            .ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);

        JsonElement schemes = document.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes");

        JsonElement bearer = schemes.GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());

        JsonElement oauth2 = schemes.GetProperty("OAuth2");
        Assert.Equal("oauth2", oauth2.GetProperty("type").GetString());
        JsonElement flows = oauth2.GetProperty("flows");
        Assert.True(flows.TryGetProperty("authorizationCode", out JsonElement codeFlow));
        Assert.Contains(
            "/connect/authorize",
            codeFlow.GetProperty("authorizationUrl").GetString() ?? string.Empty,
            StringComparison.Ordinal);
        Assert.Contains(
            "/connect/token",
            codeFlow.GetProperty("tokenUrl").GetString() ?? string.Empty,
            StringComparison.Ordinal);
        Assert.True(flows.TryGetProperty("clientCredentials", out JsonElement ccFlow));
        Assert.Contains(
            "/connect/token",
            ccFlow.GetProperty("tokenUrl").GetString() ?? string.Empty,
            StringComparison.Ordinal);

        Assert.False(schemes.TryGetProperty("ActorId", out _));

        JsonElement requirement = Assert.Single(
            document.RootElement
                .GetProperty("paths")
                .GetProperty("/api/formDefinitions")
                .GetProperty("get")
                .GetProperty("security")
                .EnumerateArray());
        Assert.True(requirement.TryGetProperty("Bearer", out _));
        Assert.True(requirement.TryGetProperty("HospitalCode", out _));

        JsonElement listing = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/me/hospitals")
            .GetProperty("get");
        JsonElement listingRequirement = Assert.Single(
            listing.GetProperty("security").EnumerateArray());
        Assert.True(listingRequirement.TryGetProperty("Bearer", out _));
        Assert.False(listingRequirement.TryGetProperty("HospitalCode", out _));
        Assert.False(listing.TryGetProperty("parameters", out _));

        JsonElement tokenOp = document.RootElement
            .GetProperty("paths")
            .GetProperty("/connect/token")
            .GetProperty("post");
        Assert.False(tokenOp.TryGetProperty("security", out _));
        Assert.False(tokenOp.TryGetProperty("parameters", out _));
    }

    [Fact]
    public async Task OpenApiDocument_Stage2Inventory_IsComplete()
    {
        JsonElement paths = await GetDocumentPathsAsync().ConfigureAwait(false);
        EndpointInventory inventory =
            InspectEndpoints(paths, Stage2Endpoints, requireJsonBodies: false);
        AssertInventoryComplete("Stage 2", inventory);
    }

    /// <summary>
    /// Every Stage 3 mutation must declare an application/json request body so
    /// client generators produce a usable payload contract.
    /// </summary>
    [Fact]
    public async Task OpenApiDocument_Stage3Inventory_IsComplete()
    {
        JsonElement paths = await GetDocumentPathsAsync().ConfigureAwait(false);
        EndpointInventory inventory =
            InspectEndpoints(paths, Stage3Endpoints, requireJsonBodies: true);
        AssertInventoryComplete("Stage 3", inventory);
    }

    [Fact]
    public async Task OpenApiDocument_UsesForwardedHttpsSchemeOnRender()
    {
        await using var renderFactory = new CynaraWebApplicationFactory(
            DatabaseSettings,
            new CynaraWebApplicationFactoryOptions
            {
                EmulateRenderProxy = true,
            });
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
    private async Task<JsonElement> GetDocumentPathsAsync()
    {
        using HttpResponseMessage response = await Client
            .GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative))
            .ConfigureAwait(false);
        string body = await response.Content
            .ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("paths").Clone();
    }

    private static void AssertInventoryComplete(
        string stage,
        EndpointInventory inventory)
    {
        Assert.True(
            inventory.Missing.Count == 0,
            $"Missing {stage} endpoints: {string.Join(", ", inventory.Missing)}");
        Assert.True(
            inventory.NoResponses.Count == 0,
            $"{stage} endpoints without documented responses: "
            + string.Join(", ", inventory.NoResponses));
        Assert.True(
            inventory.NoSuccess.Count == 0,
            $"{stage} endpoints without a 2xx response: "
            + string.Join(", ", inventory.NoSuccess));
        Assert.True(
            inventory.UnexpectedMediaTypes.Count == 0,
            $"Unexpected {stage} response media types: "
            + string.Join(", ", inventory.UnexpectedMediaTypes));
        Assert.True(
            inventory.DuplicateOperationIds.Count == 0,
            $"Duplicate {stage} operationIds: "
            + string.Join(", ", inventory.DuplicateOperationIds));
        Assert.True(
            inventory.MutationsWithoutJsonBody.Count == 0,
            $"{stage} mutations without an application/json request body: "
            + string.Join(", ", inventory.MutationsWithoutJsonBody));
    }

    private static EndpointInventory InspectEndpoints(
        JsonElement paths,
        IEnumerable<(string Path, string Method)> endpoints,
        bool requireJsonBodies)
    {
        var inventory = new EndpointInventory([], [], [], [], [], []);
        var seenOperationIds = new HashSet<string>(StringComparer.Ordinal);

        foreach ((string path, string method) in endpoints)
        {
            string label = Label(method, path);
            if (!paths.TryGetProperty(path, out JsonElement pathItem)
                || !pathItem.TryGetProperty(method, out JsonElement operation))
            {
                inventory.Missing.Add(label);
                continue;
            }

            TrackOperationId(operation, seenOperationIds, inventory);

            if (!operation.TryGetProperty("responses", out JsonElement responses))
            {
                inventory.NoResponses.Add(label);
                continue;
            }

            if (!HasSuccessResponse(responses))
            {
                inventory.NoSuccess.Add(label);
            }

            CollectUnexpectedMediaTypes(
                responses,
                label,
                inventory.UnexpectedMediaTypes);

            if (requireJsonBodies
                && method is "post"
                && IsMissingRequiredJsonBody(operation))
            {
                inventory.MutationsWithoutJsonBody.Add(label);
            }
        }

        return inventory;
    }

    private static string Label(string method, string path)
    {
        return $"{method.ToUpperInvariant()} {path}";
    }

    private static void TrackOperationId(
        JsonElement operation,
        HashSet<string> seenOperationIds,
        EndpointInventory inventory)
    {
        if (!operation.TryGetProperty("operationId", out JsonElement operationId))
        {
            return;
        }

        string id = operationId.GetString() ?? string.Empty;
        if (!seenOperationIds.Add(id))
        {
            inventory.DuplicateOperationIds.Add(id);
        }
    }

    private static bool HasSuccessResponse(JsonElement responses)
    {
        return responses.EnumerateObject().Any(item =>
            item.Name is "200" or "201" or "204");
    }

    private static bool IsMissingRequiredJsonBody(JsonElement operation)
    {
        return !operation.TryGetProperty("requestBody", out JsonElement requestBody)
            || !requestBody.TryGetProperty("required", out JsonElement required)
            || required.ValueKind != JsonValueKind.True
            || !requestBody.TryGetProperty("content", out JsonElement content)
            || !content.TryGetProperty("application/json", out _);
    }

    private static void CollectUnexpectedMediaTypes(
        JsonElement responses,
        string label,
        List<string> violations)
    {
        foreach (JsonProperty response in responses.EnumerateObject())
        {
            if (!response.Value.TryGetProperty("content", out JsonElement content))
            {
                continue;
            }

            foreach (JsonProperty mediaType in content.EnumerateObject())
            {
                if (!IsAllowedMediaType(mediaType.Name))
                {
                    violations.Add($"{label}: {mediaType.Name}");
                }
            }
        }
    }

    private sealed record EndpointInventory(
        List<string> Missing,
        List<string> NoResponses,
        List<string> NoSuccess,
        List<string> UnexpectedMediaTypes,
        List<string> DuplicateOperationIds,
        List<string> MutationsWithoutJsonBody);

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

    /// <summary>
    /// The full Stage 3 route map: the workflow pipeline runtime and the
    /// clinical task catalog/lifecycle. Every entry must exist in the OpenAPI
    /// document with documented responses, only the supported media types,
    /// and — for mutations — an application/json request body.
    /// </summary>
    private static readonly (string Path, string Method)[] Stage3Endpoints =
    [
        ("/api/pipelines", "get"),
        ("/api/pipelines", "post"),
        ("/api/pipelines/journey", "get"),
        ("/api/pipelines/{id}", "get"),
        ("/api/pipelines/{id}/history", "get"),
        ("/api/pipelines/{id}/advance", "post"),
        ("/api/pipelines/{id}/complete", "post"),
        ("/api/pipelines/{id}/cancel", "post"),
        ("/api/pipelines/{id}/enter-in-error", "post"),
        ("/api/tasks", "get"),
        ("/api/tasks/{id}", "get"),
        ("/api/tasks/{id}/claim", "post"),
        ("/api/tasks/{id}/complete", "post"),
        ("/api/tasks/{id}/cancel", "post"),
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
