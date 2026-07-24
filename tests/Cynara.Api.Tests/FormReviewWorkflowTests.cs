using System.Globalization;
using System.Net;
using System.Text.Json;

using Cynara.Api.Tests.Support;
using Cynara.Domain.Audit;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Api.Tests;

public sealed class FormReviewWorkflowTests : IDisposable
{
    public FormReviewWorkflowTests()
    {
        Client = Factory.CreateClient();
        Client.AcceptJsonApi();
        Client.DefaultRequestHeaders.Add("X-Actor-Id", "reviewer-1");
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
    public async Task SubmitForReview_WithInvalidDependencies_Fails()
    {
        string clinical = FormWithComponentRef(
            "section",
            "section.patient",
            "missing-component",
            "9.9.9");
        string definitionId = await Workflow.CreateFormDefinitionAsync(
            "broken-review",
            "Broken review",
            clinical).ConfigureAwait(false);
        string draftId = await Workflow.GetFormDraftIdAsync(definitionId)
            .ConfigureAwait(false);
        uint rowVersion = await Workflow.GetRowVersionAsync("formVersions", draftId)
            .ConfigureAwait(false);

        using HttpResponseMessage response = await Api.PostActionRawAsync(
            $"/api/formVersions/{draftId}/submit-review",
            new { rowVersion }).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.Contains("COMPONENT_VERSION_NOT_FOUND", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publish_FromDraftWithoutReview_Fails()
    {
        string definitionId = await Workflow.CreateFormDefinitionAsync(
            "direct-publish",
            "Direct publish",
            JsonApiWorkflow.MinimalClinicalSchema("notes", "form.notes"))
            .ConfigureAwait(false);
        string draftId = await Workflow.GetFormDraftIdAsync(definitionId)
            .ConfigureAwait(false);
        uint rowVersion = await Workflow.GetRowVersionAsync("formVersions", draftId)
            .ConfigureAwait(false);

        using HttpResponseMessage response = await Api.PostActionRawAsync(
            $"/api/formVersions/{draftId}/publish",
            new { rowVersion }).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RejectReview_ReturnsToDraftWithComment()
    {
        string definitionId = await Workflow.CreateFormDefinitionAsync(
            "reject-flow",
            "Reject flow",
            JsonApiWorkflow.MinimalClinicalSchema("notes", "form.notes"))
            .ConfigureAwait(false);
        string draftId = await Workflow.GetFormDraftIdAsync(definitionId)
            .ConfigureAwait(false);
        uint rowVersion = await Workflow.GetRowVersionAsync("formVersions", draftId)
            .ConfigureAwait(false);

        using JsonDocument inReview = await Api.PostActionAsync(
            $"/api/formVersions/{draftId}/submit-review",
            new { rowVersion }).ConfigureAwait(false);

        using JsonDocument rejected = await Api.PostActionAsync(
            $"/api/formVersions/{draftId}/reject-review",
            new
            {
                comment = "Needs clearer field labels.",
                rowVersion = JsonApiClient.AttrUInt(inReview, "rowVersion"),
            }).ConfigureAwait(false);

        Assert.Equal("draft", JsonApiClient.AttrString(rejected, "status"));
        Assert.Equal("rejected", JsonApiClient.AttrString(rejected, "lastReviewDecision"));
        Assert.Equal(
            "Needs clearer field labels.",
            JsonApiClient.AttrString(rejected, "lastReviewComment"));
        Assert.False(string.IsNullOrWhiteSpace(
            JsonApiClient.AttrString(rejected, "lastReviewedAt")));
    }

    [Fact]
    public async Task Publish_RecordsActorTimestampSchemaVersionAndHash()
    {
        string definitionId = await Workflow.CreateFormDefinitionAsync(
            "publish-metadata",
            "Publish metadata",
            JsonApiWorkflow.MinimalClinicalSchema("notes", "form.notes"))
            .ConfigureAwait(false);
        string draftId = await Workflow.GetFormDraftIdAsync(definitionId)
            .ConfigureAwait(false);
        using JsonDocument published = await Workflow.SubmitAndPublishFormAsync(draftId)
            .ConfigureAwait(false);

        Assert.Equal("published", JsonApiClient.AttrString(published, "status"));
        Assert.Equal("1.0.0", JsonApiClient.AttrString(published, "version"));
        Assert.Equal(
            "1.0.0",
            JsonApiClient.AttrString(published, "publishedSchemaVersion"));
        Assert.False(string.IsNullOrWhiteSpace(
            JsonApiClient.AttrString(published, "contentHash")));
        Assert.Equal("approved", JsonApiClient.AttrString(published, "lastReviewDecision"));
        Assert.False(string.IsNullOrWhiteSpace(
            JsonApiClient.AttrString(published, "publishedAt")));
        Assert.False(string.IsNullOrWhiteSpace(
            JsonApiClient.AttrString(published, "lastReviewedAt")));

        var publishedId = Guid.Parse(
            JsonApiClient.RequireId(published),
            CultureInfo.InvariantCulture);
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        AuditEvent? publishedEvent = await dbContext.AuditEvents.SingleOrDefaultAsync(
            item => item.ResourceId == publishedId
                && item.Action == "form.version.published")
            .ConfigureAwait(false);
        Assert.NotNull(publishedEvent);
        Assert.Equal("reviewer-1", publishedEvent.ActorId);
        Assert.Contains(
            "schemaVersion",
            publishedEvent.MetadataJson,
            StringComparison.Ordinal);
        Assert.Contains(
            "contentHash",
            publishedEvent.MetadataJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetiredVersion_CannotCreateNewResponse_ButRemainsResolvable()
    {
        string definitionId = await Workflow.CreateFormDefinitionAsync(
            "retired-responses",
            "Retired responses",
            JsonApiWorkflow.MinimalClinicalSchema("notes", "form.notes"))
            .ConfigureAwait(false);
        string draftId = await Workflow.GetFormDraftIdAsync(definitionId)
            .ConfigureAwait(false);
        using JsonDocument published = await Workflow.SubmitAndPublishFormAsync(draftId)
            .ConfigureAwait(false);
        string versionId = JsonApiClient.RequireId(published);

        using JsonDocument retired = await Api.PostActionAsync(
            $"/api/formVersions/{versionId}/retire",
            body: null).ConfigureAwait(false);
        Assert.Equal("retired", JsonApiClient.AttrString(retired, "status"));

        using var createContent = JsonApiClient.CreateJsonApiContent(new
        {
            data = new
            {
                type = "formResponses",
                attributes = new { answersJson = "{}" },
                relationships = new
                {
                    formVersion = new
                    {
                        data = new { type = "formVersions", id = versionId },
                    },
                },
            },
        });
        using HttpResponseMessage blocked = await Client.PostAsync(
            new Uri("/api/formResponses", UriKind.Relative),
            createContent).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, blocked.StatusCode);

        using JsonDocument resolved = await Api.GetAsync(
            $"/api/formVersions/{versionId}").ConfigureAwait(false);
        Assert.Equal("retired", JsonApiClient.AttrString(resolved, "status"));
    }

    private HttpClient Client { get; }

    private JsonApiClient Api { get; }

    private JsonApiWorkflow Workflow { get; }

    private FormWebApplicationFactory Factory { get; } = new();

    private static string FormWithComponentRef(
        string id,
        string code,
        string componentCode,
        string componentVersion)
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
                    type = "component-ref",
                    componentCode,
                    componentVersion,
                },
            },
        });
    }
}
