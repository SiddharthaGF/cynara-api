using System.Net;
using System.Text.Json;

using Cynara.Api.Tests.Support;

namespace Cynara.Api.Tests.Workflows;

/// <summary>
/// Cross-tenant isolation for workflow definitions and versions: one hospital
/// cannot read, list, or act on another hospital's workflow catalog through
/// the JSON:API surface.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class WorkflowTenantIsolationTests : IDisposable
{
    private const string PrimaryCode = "primary";
    private const string OtherCode = "secondary";

    public WorkflowTenantIsolationTests(PostgreSqlDatabaseFixture database)
    {
        Factory = new CynaraTenantWebApplicationFactory(database.Settings);
        Client = Factory.CreateClient();
        Client.AcceptJsonApi();
        OtherClient = Factory.CreateClient();
        OtherClient.AcceptJsonApi();

        Client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            PrimaryCode);
        OtherClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            OtherCode);

        Factory.EnsureBootstrapHospitalAsync().GetAwaiter().GetResult();
        Factory.SeedSecondaryHospitalAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        Client.Dispose();
        OtherClient.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task CrossTenant_DefinitionAndVersion_AreNotFound()
    {
        var primaryApi = new JsonApiClient(Client);
        using JsonDocument created = await primaryApi.PostResourceAsync(
            "workflowDefinitions",
            new
            {
                code = "isolation-workflow",
                name = "Isolation workflow",
                initialWorkflowSchemaJson = WorkflowTestSchemas.Minimal(),
            }).ConfigureAwait(false);
        string definitionId = JsonApiClient.RequireId(created);

        string draftId = await GetDraftIdAsync(primaryApi, definitionId)
            .ConfigureAwait(false);

        using HttpResponseMessage definition = await OtherClient
            .GetAsync(
                new Uri(
                    $"/api/workflowDefinitions/{definitionId}",
                    UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, definition.StatusCode);

        using HttpResponseMessage version = await OtherClient
            .GetAsync(
                new Uri($"/api/workflowVersions/{draftId}", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, version.StatusCode);
    }

    [Fact]
    public async Task CrossTenant_Collection_IsScoped()
    {
        var primaryApi = new JsonApiClient(Client);
        _ = await primaryApi.PostResourceAsync(
            "workflowDefinitions",
            new
            {
                code = "scoped-workflow",
                name = "Scoped workflow",
                initialWorkflowSchemaJson = WorkflowTestSchemas.Minimal(),
            }).ConfigureAwait(false);

        using HttpResponseMessage response = await OtherClient
            .GetAsync(new Uri("/api/workflowDefinitions", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal(JsonValueKind.Array, data.ValueKind);
        Assert.Empty(data.EnumerateArray());
    }

    [Fact]
    public async Task CrossTenant_LifecycleAction_IsNotFound()
    {
        var primaryApi = new JsonApiClient(Client);
        using JsonDocument created = await primaryApi.PostResourceAsync(
            "workflowDefinitions",
            new
            {
                code = "guarded-workflow",
                name = "Guarded workflow",
                initialWorkflowSchemaJson = WorkflowTestSchemas.Minimal(),
            }).ConfigureAwait(false);
        string definitionId = JsonApiClient.RequireId(created);
        string draftId = await GetDraftIdAsync(primaryApi, definitionId)
            .ConfigureAwait(false);

        using HttpResponseMessage submit = await OtherClient
            .PostAsync(
                new Uri(
                    $"/api/workflowVersions/{draftId}/submit-review?rowVersion=0",
                    UriKind.Relative),
                content: null)
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, submit.StatusCode);
    }

    private CynaraTenantWebApplicationFactory Factory { get; }

    private HttpClient Client { get; }

    private HttpClient OtherClient { get; }

    private static async Task<string> GetDraftIdAsync(
        JsonApiClient api,
        string definitionId)
    {
        using JsonDocument definition = await api.GetAsync(
            $"/api/workflowDefinitions/{definitionId}?include=versions")
            .ConfigureAwait(false);
        return definition.RootElement.GetProperty("included")
            .EnumerateArray()
            .First(item => string.Equals(
                item.GetProperty("attributes").GetProperty("status").GetString(),
                "draft",
                StringComparison.OrdinalIgnoreCase))
            .GetProperty("id")
            .GetString()!;
    }
}
