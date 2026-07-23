using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Cynara.Application.Audit;
using Cynara.Application.Forms;

using Xunit;

namespace Cynara.Api.Tests;

public sealed class AuditQueryTests : IDisposable
{
    public AuditQueryTests()
    {
        Client = Factory.CreateClient();
        Client.DefaultRequestHeaders.Add("X-Actor-Id", "auditor-1");
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ListAuditEvents_RequiresAtLeastOneFilter()
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri("/api/audit/events", UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListAuditEvents_ByResourceId_ReturnsFormLifecycleEvents()
    {
        var createRequest = new CreateFormRequest(
            "audit-form",
            "Audit form",
            MinimalClinicalSchema("notes", "form.notes"),
UiSchemaJson: null);

        using HttpResponseMessage createResponse = await Client.PostAsJsonAsync("/api/forms", createRequest).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        using HttpResponseMessage actorEventsResponse = await Client.GetAsync(
            new Uri("/api/audit/events?actorId=auditor-1&resourceType=form-definition&limit=50", UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, actorEventsResponse.StatusCode);

        List<AuditEventDto> actorEvents =
            (await actorEventsResponse.Content.ReadFromJsonAsync<List<AuditEventDto>>().ConfigureAwait(false))!;
        AuditEventDto createdEvent = Assert.Single(
            actorEvents,
            item => string.Equals(item.Action, "form.created"
, StringComparison.Ordinal) && item.MetadataJson!.Contains("audit-form", StringComparison.Ordinal));

        using HttpResponseMessage auditResponse = await Client.GetAsync(
            new Uri($"/api/audit/events?resourceType=form-definition&resourceId={createdEvent.ResourceId}", UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);

        List<AuditEventDto> events = (await auditResponse.Content.ReadFromJsonAsync<List<AuditEventDto>>().ConfigureAwait(false))!;
        Assert.Contains(events, item => string.Equals(item.Action, "form.created", StringComparison.Ordinal));
        Assert.All(events, item => Assert.Equal(createdEvent.ResourceId, item.ResourceId));
    }

    [Fact]
    public async Task SoftDeleteResponse_RecordsActorAndReasonInAuditTrail()
    {
        FormResponseDto response = await CreatePublishedResponseAsync("audit-response").ConfigureAwait(false);

        using HttpResponseMessage deleteResponse = await Client.DeleteAsync(
            new Uri($"/api/responses/{response.Id}?reason=Entered%20in%20error", UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using HttpResponseMessage auditResponse = await Client.GetAsync(
            new Uri($"/api/audit/events?resourceType=form-response&resourceId={response.Id}", UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);

        List<AuditEventDto> events = (await auditResponse.Content.ReadFromJsonAsync<List<AuditEventDto>>().ConfigureAwait(false))!;
        AuditEventDto deletedEvent = Assert.Single(events, item => string.Equals(item.Action, "response.draft.deleted", StringComparison.Ordinal));
        Assert.Equal("auditor-1", deletedEvent.ActorId);
        Assert.Contains("Entered in error", deletedEvent.MetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListAuditEvents_ByActorId_ReturnsMatchingEvents()
    {
        await CreatePublishedResponseAsync("actor-query").ConfigureAwait(false);

        using HttpResponseMessage auditResponse = await Client.GetAsync(new Uri("/api/audit/events?actorId=auditor-1", UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);

        List<AuditEventDto> events = (await auditResponse.Content.ReadFromJsonAsync<List<AuditEventDto>>().ConfigureAwait(false))!;
        Assert.NotEmpty(events);
        Assert.All(events, item => Assert.Equal("auditor-1", item.ActorId));
    }

    private HttpClient Client { get; }

    private FormWebApplicationFactory Factory { get; } = new();

    private async Task<FormResponseDto> CreatePublishedResponseAsync(string code)
    {
        var createFormRequest = new CreateFormRequest(
            code,
            code,
            MinimalClinicalSchema("field", $"{code}.field"),
UiSchemaJson: null);
        using HttpResponseMessage createFormResponse = await Client.PostAsJsonAsync("/api/forms", createFormRequest).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, createFormResponse.StatusCode);

        FormVersionDto draft = await GetEditableVersionAsync(code).ConfigureAwait(false);
        FormVersionDto inReview = await SubmitForReviewAsync(code, draft.RowVersion).ConfigureAwait(false);

        using HttpResponseMessage publishResponse = await Client.PostAsJsonAsync(
            $"/api/forms/{code}/draft/publish",
            new PublishFormDraftRequest(inReview.RowVersion)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);

        using HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            $"/api/forms/{code}/versions/1.0.0/responses",
            new CreateFormResponseRequest()).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        return (await createResponse.Content.ReadFromJsonAsync<FormResponseDto>().ConfigureAwait(false))!;
    }

    private async Task<FormVersionDto> GetEditableVersionAsync(string code)
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri($"/api/forms/{code}/draft", UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<FormVersionDto>().ConfigureAwait(false))!;
    }

    private async Task<FormVersionDto> SubmitForReviewAsync(string code, uint rowVersion)
    {
        using HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/api/forms/{code}/draft/submit-review",
            new SubmitFormDraftForReviewRequest(rowVersion)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<FormVersionDto>().ConfigureAwait(false))!;
    }

    private static string MinimalClinicalSchema(string id, string code)
    {
        return JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0.0",
            fields = new[]
            {
                new
                {
                    id,
                    code,
                    type = "text",
                    maxLength = 500,
                },
            },
        });
    }
}
