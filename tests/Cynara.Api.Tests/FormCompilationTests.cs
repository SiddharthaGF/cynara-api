using System.Net;
using System.Text.Json;

using Cynara.Api.Tests.Support;

namespace Cynara.Api.Tests;

[Collection(PostgresFixtureDefinition.Name)]
public sealed class FormCompilationTests : IDisposable
{
    public FormCompilationTests(PostgreSqlDatabaseFixture database)
    {
        Factory = new FormWebApplicationFactory(database.Settings);
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
    public async Task PublishDraft_InlinesComponentReferencesIntoCompiledSnapshot()
    {
        await CreateAndPublishComponentAsync(
            "patient-demographics",
            JsonApiWorkflow.MinimalClinicalSchema("patient-name", "patient.name"),
            JsonApiWorkflow.MinimalUiSchema("patient-name", "Patient name"))
            .ConfigureAwait(false);

        string formClinical = FormWithComponentRef(
            "patient-section",
            "section.patient",
            "patient-demographics",
            "1.0.0");
        string formUi = FormUiWithComponentRef("patient-section", "Demographics");

        string definitionId = await Workflow.CreateFormDefinitionAsync(
            "intake-form",
            "Intake form",
            formClinical,
            formUi).ConfigureAwait(false);
        string draftId = await Workflow.GetFormDraftIdAsync(definitionId)
            .ConfigureAwait(false);
        using JsonDocument published = await Workflow.SubmitAndPublishFormAsync(draftId)
            .ConfigureAwait(false);

        Assert.Equal("published", JsonApiClient.AttrString(published, "status"));
        Assert.DoesNotContain(
            "component-ref",
            JsonApiClient.AttrString(published, "clinicalSchemaJson"),
            StringComparison.Ordinal);
        Assert.Contains(
            "\"type\":\"group\"",
            JsonApiClient.AttrString(published, "clinicalSchemaJson"),
            StringComparison.Ordinal);
        Assert.Contains(
            "patient-name",
            JsonApiClient.AttrString(published, "clinicalSchemaJson"),
            StringComparison.Ordinal);
        Assert.Contains(
            "Patient name",
            JsonApiClient.AttrString(published, "uiSchemaJson"),
            StringComparison.Ordinal);
        Assert.Contains(
            "patient-demographics",
            JsonApiClient.AttrString(published, "dependencyMetadataJson"),
            StringComparison.Ordinal);
        Assert.Contains(
            "1.0.0",
            JsonApiClient.AttrString(published, "dependencyMetadataJson"),
            StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(
            JsonApiClient.AttrString(published, "contentHash")));
    }

    [Fact]
    public async Task PublishDraft_PublishedFormDoesNotChangeWhenComponentReceivesNewVersion()
    {
        string componentDefinitionId = await CreateAndPublishComponentAsync(
            "vitals-panel",
            JsonApiWorkflow.MinimalClinicalSchema("heart-rate", "vital.heart-rate"),
            JsonApiWorkflow.MinimalUiSchema("heart-rate", "Heart rate"))
            .ConfigureAwait(false);

        string formClinical = FormWithComponentRef(
            "vitals",
            "section.vitals",
            "vitals-panel",
            "1.0.0");
        string formDefinitionId = await Workflow.CreateFormDefinitionAsync(
            "vitals-form",
            "Vitals form",
            formClinical).ConfigureAwait(false);
        string formDraftId = await Workflow.GetFormDraftIdAsync(formDefinitionId)
            .ConfigureAwait(false);
        using JsonDocument published = await Workflow.SubmitAndPublishFormAsync(formDraftId)
            .ConfigureAwait(false);
        string publishedClinical = JsonApiClient.AttrString(published, "clinicalSchemaJson")!;
        string publishedHash = JsonApiClient.AttrString(published, "contentHash")!;
        string publishedId = JsonApiClient.RequireId(published);

        using HttpResponseMessage createDraft = await Client.PostAsync(
            new Uri(
                $"/api/componentDefinitions/{componentDefinitionId}/create-draft",
                UriKind.Relative),
            content: null).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, createDraft.StatusCode);

        string componentDraftId = await Workflow.GetComponentDraftIdAsync(
            componentDefinitionId).ConfigureAwait(false);
        using JsonDocument componentDraft = await Workflow.GetVersionAsync(
            "componentVersions",
            componentDraftId).ConfigureAwait(false);
        using JsonDocument unusedDoc = await Api.PatchResourceAsync(
            "componentVersions",
            componentDraftId,
            new
            {
                clinicalSchemaJson = JsonApiWorkflow.MinimalClinicalSchema(
                    "heart-rate-updated",
                    "vital.heart-rate-updated"),
                uiSchemaJson = JsonApiClient.AttrString(componentDraft, "uiSchemaJson"),
                rowVersion = JsonApiClient.AttrUInt(componentDraft, "rowVersion"),
            }).ConfigureAwait(false);
        await Workflow.PublishComponentAsync(componentDraftId).ConfigureAwait(false);

        using JsonDocument resolved = await Api.GetAsync(
            $"/api/formVersions/{publishedId}").ConfigureAwait(false);
        Assert.Equal(
            publishedClinical,
            JsonApiClient.AttrString(resolved, "clinicalSchemaJson"));
        Assert.Equal(publishedHash, JsonApiClient.AttrString(resolved, "contentHash"));
        Assert.Contains(
            "heart-rate",
            JsonApiClient.AttrString(resolved, "clinicalSchemaJson"),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "heart-rate-updated",
            JsonApiClient.AttrString(resolved, "clinicalSchemaJson"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishDraft_FailsWhenComponentVersionIsMissing()
    {
        string formClinical = FormWithComponentRef(
            "patient-section",
            "section.patient",
            "missing-component",
            "9.9.9");
        string definitionId = await Workflow.CreateFormDefinitionAsync(
            "broken-form",
            "Broken form",
            formClinical).ConfigureAwait(false);
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
    public async Task PublishDraft_FailsWhenComponentVersionIsNotPinned()
    {
        await CreateAndPublishComponentAsync(
            "allergies",
            JsonApiWorkflow.MinimalClinicalSchema("allergy-list", "allergy.list"),
            uiSchemaJson: null).ConfigureAwait(false);

        string formClinical = FormWithComponentRef(
            "allergy-section",
            "section.allergies",
            "allergies",
            componentVersion: null);
        string definitionId = await Workflow.CreateFormDefinitionAsync(
            "allergy-form",
            "Allergy form",
            formClinical).ConfigureAwait(false);
        string draftId = await Workflow.GetFormDraftIdAsync(definitionId)
            .ConfigureAwait(false);
        uint rowVersion = await Workflow.GetRowVersionAsync("formVersions", draftId)
            .ConfigureAwait(false);

        using HttpResponseMessage response = await Api.PostActionRawAsync(
            $"/api/formVersions/{draftId}/submit-review",
            new { rowVersion }).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.Contains("COMPONENT_VERSION_REQUIRED", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishDraft_FailsOnCircularComponentReferences()
    {
        string definitionA = await Workflow.CreateComponentDefinitionAsync(
            "component-a",
            "Component A",
            ComponentRefClinicalSchema("ref-b", "component.b", "component-b", "1.0.0"))
            .ConfigureAwait(false);
        string definitionB = await Workflow.CreateComponentDefinitionAsync(
            "component-b",
            "Component B",
            ComponentRefClinicalSchema("ref-a", "component.a", "component-a", "1.0.0"))
            .ConfigureAwait(false);

        string draftA = await Workflow.GetComponentDraftIdAsync(definitionA)
            .ConfigureAwait(false);
        await Workflow.PublishComponentAsync(draftA).ConfigureAwait(false);
        string draftB = await Workflow.GetComponentDraftIdAsync(definitionB)
            .ConfigureAwait(false);
        await Workflow.PublishComponentAsync(draftB).ConfigureAwait(false);

        string formClinical = FormWithComponentRef(
            "section-a",
            "section.a",
            "component-a",
            "1.0.0");
        string formDefinitionId = await Workflow.CreateFormDefinitionAsync(
            "circular-form",
            "Circular form",
            formClinical).ConfigureAwait(false);
        string formDraftId = await Workflow.GetFormDraftIdAsync(formDefinitionId)
            .ConfigureAwait(false);
        uint rowVersion = await Workflow.GetRowVersionAsync("formVersions", formDraftId)
            .ConfigureAwait(false);

        using HttpResponseMessage response = await Api.PostActionRawAsync(
            $"/api/formVersions/{formDraftId}/submit-review",
            new { rowVersion }).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.Contains("CIRCULAR_COMPONENT_REFERENCE", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishDraft_ProducesDeterministicSnapshotAndHash()
    {
        await CreateAndPublishComponentAsync(
            "consent-block",
            JsonApiWorkflow.MinimalClinicalSchema("consent-given", "consent.given"),
            JsonApiWorkflow.MinimalUiSchema("consent-given", "Consent given"))
            .ConfigureAwait(false);

        string formClinical = FormWithComponentRef(
            "consent-section",
            "section.consent",
            "consent-block",
            "1.0.0");
        string definitionA = await Workflow.CreateFormDefinitionAsync(
            "consent-form-a",
            "Consent form A",
            formClinical).ConfigureAwait(false);
        string definitionB = await Workflow.CreateFormDefinitionAsync(
            "consent-form-b",
            "Consent form B",
            formClinical).ConfigureAwait(false);

        string draftA = await Workflow.GetFormDraftIdAsync(definitionA).ConfigureAwait(false);
        string draftB = await Workflow.GetFormDraftIdAsync(definitionB).ConfigureAwait(false);
        using JsonDocument publishedA = await Workflow.SubmitAndPublishFormAsync(draftA)
            .ConfigureAwait(false);
        using JsonDocument publishedB = await Workflow.SubmitAndPublishFormAsync(draftB)
            .ConfigureAwait(false);

        Assert.Equal(
            JsonApiClient.AttrString(publishedA, "clinicalSchemaJson"),
            JsonApiClient.AttrString(publishedB, "clinicalSchemaJson"));
        Assert.Equal(
            JsonApiClient.AttrString(publishedA, "uiSchemaJson"),
            JsonApiClient.AttrString(publishedB, "uiSchemaJson"));
        Assert.Equal(
            JsonApiClient.AttrString(publishedA, "dependencyMetadataJson"),
            JsonApiClient.AttrString(publishedB, "dependencyMetadataJson"));
        Assert.Equal(
            JsonApiClient.AttrString(publishedA, "contentHash"),
            JsonApiClient.AttrString(publishedB, "contentHash"));
    }

    private HttpClient Client { get; }

    private JsonApiClient Api { get; }

    private JsonApiWorkflow Workflow { get; }

    private FormWebApplicationFactory Factory { get; }

    private async Task<string> CreateAndPublishComponentAsync(
        string code,
        string clinicalSchemaJson,
        string? uiSchemaJson)
    {
        string definitionId = await Workflow.CreateComponentDefinitionAsync(
            code,
            code,
            clinicalSchemaJson,
            uiSchemaJson).ConfigureAwait(false);
        string draftId = await Workflow.GetComponentDraftIdAsync(definitionId)
            .ConfigureAwait(false);
        await Workflow.PublishComponentAsync(draftId).ConfigureAwait(false);
        return definitionId;
    }

    private static string FormWithComponentRef(
        string id,
        string code,
        string componentCode,
        string? componentVersion)
    {
        var field = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["code"] = code,
            ["type"] = "component-ref",
            ["componentCode"] = componentCode,
        };

        if (componentVersion is not null)
        {
            field["componentVersion"] = componentVersion;
        }

        return JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0.0",
            fields = new object[] { field },
        });
    }

    private static string FormUiWithComponentRef(string fieldId, string label)
    {
        return JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0.0",
            clinicalSchemaVersion = "1.0.0",
            fields = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [fieldId] = new
                {
                    label,
                    widget = "component",
                },
            },
            layout = new object[]
            {
                new
                {
                    type = "field",
                    fieldId,
                },
            },
        });
    }

    private static string ComponentRefClinicalSchema(
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
