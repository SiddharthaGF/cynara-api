using System.Net;
using System.Text.Json;

using Cynara.Api.Tests.Support;

namespace Cynara.Api.Tests;

public sealed class AuditQueryTests : IDisposable
{
    public AuditQueryTests()
    {
        Client = Factory.CreateClient();
        Client.AcceptJsonApi();
        Client.DefaultRequestHeaders.Add("X-Actor-Id", "auditor-1");
        Api = new JsonApiClient(Client);
        Api.UseHospitalContext(Factory.BootstrapOptions.BootstrapCode);
        Workflow = new JsonApiWorkflow(Api, Client);
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ListAuditEvents_WithoutFilter_ReturnsCollection()
    {
        // JSON:API allows unfiltered reads; ensure the resource route is wired.
        using JsonDocument document = await Api.GetAsync("/api/auditEvents")
            .ConfigureAwait(false);
        Assert.True(document.RootElement.TryGetProperty("data", out _));
    }

    [Fact]
    public async Task ListAuditEvents_ByResourceId_ReturnsFormLifecycleEvents()
    {
        string definitionId = await Workflow.CreateFormDefinitionAsync(
            "audit-form",
            "Audit form",
            JsonApiWorkflow.MinimalClinicalSchema("notes", "form.notes"))
            .ConfigureAwait(false);

        using JsonDocument actorEvents = await Api.GetAsync(
            "/api/auditEvents?filter=equals(actorId,'auditor-1')"
            + "&filter=equals(resourceType,'form-definition')")
            .ConfigureAwait(false);
        JsonElement created = Assert.Single(
            actorEvents.RootElement.GetProperty("data").EnumerateArray(),
            item => string.Equals(
                item.GetProperty("attributes").GetProperty("action").GetString(),
                "form.created",
                StringComparison.Ordinal)
                && (item.GetProperty("attributes").GetProperty("metadataJson")
                    .GetString() ?? string.Empty)
                    .Contains("audit-form", StringComparison.Ordinal));

        string resourceId = created.GetProperty("attributes")
            .GetProperty("resourceId")
            .GetString()!;
        Assert.Equal(definitionId, resourceId);

        using JsonDocument auditResponse = await Api.GetAsync(
            "/api/auditEvents?filter=equals(resourceType,'form-definition')"
            + $"&filter=equals(resourceId,'{resourceId}')")
            .ConfigureAwait(false);
        JsonElement events = auditResponse.RootElement.GetProperty("data");
        Assert.Contains(
            events.EnumerateArray(),
            item => string.Equals(
                item.GetProperty("attributes").GetProperty("action").GetString(),
                "form.created",
                StringComparison.Ordinal));
        Assert.All(
            events.EnumerateArray(),
            item => Assert.Equal(
                resourceId,
                item.GetProperty("attributes").GetProperty("resourceId").GetString()));
    }

    [Fact]
    public async Task SoftDeleteResponse_RecordsActorAndReasonInAuditTrail()
    {
        (_, string versionId) = await Workflow.PublishFormAsync(
            "audit-response",
            "audit-response",
            JsonApiWorkflow.MinimalClinicalSchema("field", "audit-response.field"))
            .ConfigureAwait(false);
        using JsonDocument response = await Workflow.CreateResponseAsync(versionId)
            .ConfigureAwait(false);
        string responseId = JsonApiClient.RequireId(response);

        using HttpResponseMessage deleteResponse = await Api.DeleteAsync(
            $"/api/formResponses/{responseId}?reason=Entered%20in%20error")
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using JsonDocument auditResponse = await Api.GetAsync(
            "/api/auditEvents?filter=equals(resourceType,'form-response')"
            + $"&filter=equals(resourceId,'{responseId}')")
            .ConfigureAwait(false);
        JsonElement deletedEvent = Assert.Single(
            auditResponse.RootElement.GetProperty("data").EnumerateArray(),
            item => string.Equals(
                item.GetProperty("attributes").GetProperty("action").GetString(),
                "response.draft.deleted",
                StringComparison.Ordinal));
        Assert.Equal(
            "auditor-1",
            deletedEvent.GetProperty("attributes").GetProperty("actorId").GetString());
        Assert.Contains(
            "Entered in error",
            deletedEvent.GetProperty("attributes").GetProperty("metadataJson")
                .GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListAuditEvents_ByActorId_ReturnsMatchingEvents()
    {
        (_, string versionId) = await Workflow.PublishFormAsync(
            "actor-query",
            "actor-query",
            JsonApiWorkflow.MinimalClinicalSchema("field", "actor-query.field"))
            .ConfigureAwait(false);
        using JsonDocument createdResponse = await Workflow.CreateResponseAsync(versionId)
            .ConfigureAwait(false);
        Assert.NotNull(JsonApiClient.RequireId(createdResponse));

        using JsonDocument auditResponse = await Api.GetAsync(
            "/api/auditEvents?filter=equals(actorId,'auditor-1')")
            .ConfigureAwait(false);
        JsonElement events = auditResponse.RootElement.GetProperty("data");
        Assert.NotEmpty(events.EnumerateArray());
        Assert.All(
            events.EnumerateArray(),
            item => Assert.Equal(
                "auditor-1",
                item.GetProperty("attributes").GetProperty("actorId").GetString()));
    }

    private HttpClient Client { get; }

    private JsonApiClient Api { get; }

    private JsonApiWorkflow Workflow { get; }

    private FormWebApplicationFactory Factory { get; } = new();
}
