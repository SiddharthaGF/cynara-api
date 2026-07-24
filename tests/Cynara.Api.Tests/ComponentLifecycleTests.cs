using System.Net;
using System.Text.Json;

using Cynara.Api.Tests.Support;
using Cynara.Domain.Audit;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Cynara.Api.Tests;

public sealed class ComponentLifecycleTests : IDisposable
{
    public ComponentLifecycleTests()
    {
        Client = Factory.CreateClient();
        Client.AcceptJsonApi();
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
    public async Task ComponentLifecycle_CreateDraftPublishRetireAndResolveHistory()
    {
        string definitionId = await Workflow.CreateComponentDefinitionAsync(
            "patient-demographics",
            "Patient demographics",
            JsonApiWorkflow.MinimalClinicalSchema("patient-name", "patient.name"),
            JsonApiWorkflow.MinimalUiSchema("patient-name")).ConfigureAwait(false);

        using JsonDocument definition = await Api.GetAsync(
            $"/api/componentDefinitions/{definitionId}").ConfigureAwait(false);
        Assert.Equal("patient-demographics", JsonApiClient.AttrString(definition, "code"));

        string draftId = await Workflow.GetComponentDraftIdAsync(definitionId)
            .ConfigureAwait(false);
        using JsonDocument draft = await Workflow.GetVersionAsync(
            "componentVersions",
            draftId).ConfigureAwait(false);
        Assert.Equal("draft", JsonApiClient.AttrString(draft, "status"));
        Assert.Equal(0u, JsonApiClient.AttrUInt(draft, "rowVersion"));

        string updatedClinical = JsonApiWorkflow.MinimalClinicalSchema(
            "patient-full-name",
            "patient.full-name");
        using JsonDocument updated = await Api.PatchResourceAsync(
            "componentVersions",
            draftId,
            new
            {
                clinicalSchemaJson = updatedClinical,
                uiSchemaJson = JsonApiClient.AttrString(draft, "uiSchemaJson"),
                rowVersion = JsonApiClient.AttrUInt(draft, "rowVersion"),
            }).ConfigureAwait(false);
        Assert.Equal(1u, JsonApiClient.AttrUInt(updated, "rowVersion"));
        Assert.Contains(
            "patient-full-name",
            JsonApiClient.AttrString(updated, "clinicalSchemaJson"),
            StringComparison.Ordinal);

        using JsonDocument published = await Api.PostActionAsync(
            $"/api/componentVersions/{draftId}/publish",
            new { rowVersion = JsonApiClient.AttrUInt(updated, "rowVersion") })
            .ConfigureAwait(false);
        Assert.Equal("published", JsonApiClient.AttrString(published, "status"));
        Assert.Equal("1.0.0", JsonApiClient.AttrString(published, "version"));
        Assert.False(string.IsNullOrWhiteSpace(
            JsonApiClient.AttrString(published, "contentHash")));

        using JsonDocument resolved = await Api.GetAsync(
            $"/api/componentVersions?filter=equals(componentDefinition.id,'{definitionId}')"
            + "&filter=equals(version,'1.0.0')")
            .ConfigureAwait(false);
        Assert.Equal(
            draftId,
            resolved.RootElement.GetProperty("data")[0].GetProperty("id").GetString());

        using JsonDocument retired = await Api.PostActionAsync(
            $"/api/componentVersions/{draftId}/retire",
            body: null).ConfigureAwait(false);
        Assert.Equal("retired", JsonApiClient.AttrString(retired, "status"));

        using JsonDocument stillResolvable = await Api.GetAsync(
            $"/api/componentVersions/{draftId}").ConfigureAwait(false);
        Assert.Equal("retired", JsonApiClient.AttrString(stillResolvable, "status"));

        await AssertAuditEventsRecordedAsync(
            Guid.Parse(draftId),
            "component.version.published",
            "component.version.retired").ConfigureAwait(false);
    }

    [Fact]
    public async Task SoftDeleteDraft_RemovesUnusedComponent()
    {
        string definitionId = await Workflow.CreateComponentDefinitionAsync(
            "unused-section",
            "Unused section",
            JsonApiWorkflow.MinimalClinicalSchema("section-notes", "section.notes"))
            .ConfigureAwait(false);

        using HttpResponseMessage deleteResponse = await Api.DeleteAsync(
            $"/api/componentDefinitions/{definitionId}/soft-delete-draft")
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using HttpResponseMessage getResponse = await Api.SendGetAsync(
            $"/api/componentDefinitions/{definitionId}").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task SoftDeleteDraft_AllowsDeleteAfterPublishedVersionIsRetired()
    {
        string definitionId = await Workflow.CreateComponentDefinitionAsync(
            "retired-only",
            "Retired only",
            JsonApiWorkflow.MinimalClinicalSchema("notes", "section.notes"))
            .ConfigureAwait(false);
        string draftId = await Workflow.GetComponentDraftIdAsync(definitionId)
            .ConfigureAwait(false);
        await Workflow.PublishComponentAsync(draftId).ConfigureAwait(false);

        using JsonDocument unusedDoc = await Api.PostActionAsync(
            $"/api/componentVersions/{draftId}/retire",
            body: null).ConfigureAwait(false);

        using HttpResponseMessage createDraft = await Client.PostAsync(
            new Uri(
                $"/api/componentDefinitions/{definitionId}/create-draft",
                UriKind.Relative),
            content: null).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, createDraft.StatusCode);

        using HttpResponseMessage deleteResponse = await Api.DeleteAsync(
            $"/api/componentDefinitions/{definitionId}/soft-delete-draft")
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using HttpResponseMessage getResponse = await Api.SendGetAsync(
            $"/api/componentDefinitions/{definitionId}").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task PublishDraft_IsImmutableAfterPublish()
    {
        string definitionId = await Workflow.CreateComponentDefinitionAsync(
            "vitals-panel",
            "Vitals panel",
            JsonApiWorkflow.MinimalClinicalSchema("vitals-systolic", "vitals.systolic"),
            JsonApiWorkflow.MinimalUiSchema("vitals-systolic")).ConfigureAwait(false);
        string draftId = await Workflow.GetComponentDraftIdAsync(definitionId)
            .ConfigureAwait(false);
        uint rowVersion = await Workflow.GetRowVersionAsync("componentVersions", draftId)
            .ConfigureAwait(false);

        using JsonDocument published = await Api.PostActionAsync(
            $"/api/componentVersions/{draftId}/publish",
            new { rowVersion }).ConfigureAwait(false);

        using var content = JsonApiClient.CreateJsonApiContent(new
        {
            data = new
            {
                type = "componentVersions",
                id = draftId,
                attributes = new
                {
                    clinicalSchemaJson = JsonApiWorkflow.MinimalClinicalSchema(
                        "vitals-diastolic",
                        "vitals.diastolic"),
                    uiSchemaJson = JsonApiClient.AttrString(published, "uiSchemaJson"),
                    rowVersion = JsonApiClient.AttrUInt(published, "rowVersion"),
                },
            },
        });
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            new Uri($"/api/componentVersions/{draftId}", UriKind.Relative))
        {
            Content = content,
        };
        using HttpResponseMessage updateResponse = await Client.SendAsync(request)
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, updateResponse.StatusCode);
    }

    private HttpClient Client { get; }

    private JsonApiClient Api { get; }

    private JsonApiWorkflow Workflow { get; }

    private ComponentWebApplicationFactory Factory { get; } = new();

    private async Task AssertAuditEventsRecordedAsync(
        Guid resourceId,
        params string[] actions)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        List<AuditEvent> events = [.. (await dbContext.AuditEvents
            .Where(item => item.ResourceId == resourceId)
            .ToListAsync()
            .ConfigureAwait(false))
            .OrderBy(item => item.OccurredAt)];

        foreach (string action in actions)
        {
            Assert.Contains(
                events,
                item => string.Equals(item.Action, action, StringComparison.Ordinal));
        }
    }
}

internal sealed class ComponentWebApplicationFactory(TestDatabaseSettings database)
    : CynaraWebApplicationFactory(database)
{
    public ComponentWebApplicationFactory()
        : this(TestDatabaseSettings.SqliteInMemory)
    {
    }
}
