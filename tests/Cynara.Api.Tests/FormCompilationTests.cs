using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Cynara.Application.Components;
using Cynara.Application.Forms;

namespace Cynara.Api.Tests;

public sealed class FormCompilationTests : IDisposable
{
    public FormCompilationTests()
    {
        Client = Factory.CreateClient();
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
            MinimalComponentClinicalSchema("patient-name", "patient.name"),
            MinimalUiSchema("patient-name", "Patient name")).ConfigureAwait(false);

        string formClinical = FormWithComponentRef("patient-section", "section.patient", "patient-demographics", "1.0.0");
        string formUi = FormUiWithComponentRef("patient-section", "Demographics");

        await CreateFormAsync("intake-form", "Intake form", formClinical, formUi).ConfigureAwait(false);
        FormVersionDto draft = await GetEditableVersionAsync("intake-form").ConfigureAwait(false);

        FormVersionDto published = await PublishDraftAsync("intake-form", draft.RowVersion).ConfigureAwait(false);

        Assert.Equal("published", published.Status);
        Assert.DoesNotContain("component-ref", published.ClinicalSchemaJson, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"group\"", published.ClinicalSchemaJson, StringComparison.Ordinal);
        Assert.Contains("patient-name", published.ClinicalSchemaJson, StringComparison.Ordinal);
        Assert.Contains("Patient name", published.UiSchemaJson, StringComparison.Ordinal);
        Assert.NotNull(published.DependencyMetadataJson);
        Assert.Contains("patient-demographics", published.DependencyMetadataJson, StringComparison.Ordinal);
        Assert.Contains("1.0.0", published.DependencyMetadataJson, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(published.ContentHash));
    }

    [Fact]
    public async Task PublishDraft_PublishedFormDoesNotChangeWhenComponentReceivesNewVersion()
    {
        await CreateAndPublishComponentAsync(
            "vitals-panel",
            MinimalComponentClinicalSchema("heart-rate", "vital.heart-rate"),
            MinimalUiSchema("heart-rate", "Heart rate")).ConfigureAwait(false);

        string formClinical = FormWithComponentRef("vitals", "section.vitals", "vitals-panel", "1.0.0");
        await CreateFormAsync("vitals-form", "Vitals form", formClinical, uiSchemaJson: null).ConfigureAwait(false);
        FormVersionDto draft = await GetEditableVersionAsync("vitals-form").ConfigureAwait(false);
        FormVersionDto published = await PublishDraftAsync("vitals-form", draft.RowVersion).ConfigureAwait(false);

        using HttpResponseMessage createComponentDraftResponse = await Client.PostAsync(
            new Uri("/api/components/vitals-panel/draft", UriKind.Relative),
            content: null).ConfigureAwait(false);
        await AssertStatusAsync(createComponentDraftResponse, HttpStatusCode.Created).ConfigureAwait(false);

        ComponentVersionDto componentDraft = await GetComponentDraftAsync("vitals-panel").ConfigureAwait(false);
        string updatedClinical = MinimalComponentClinicalSchema("heart-rate-updated", "vital.heart-rate-updated");
        await UpdateComponentDraftAsync("vitals-panel", updatedClinical, componentDraft.UiSchemaJson, componentDraft.RowVersion).ConfigureAwait(false);
        componentDraft = await GetComponentDraftAsync("vitals-panel").ConfigureAwait(false);
        await PublishComponentDraftAsync("vitals-panel", componentDraft.RowVersion).ConfigureAwait(false);

        FormVersionDto resolved = await GetVersionAsync("vitals-form", published.Version!).ConfigureAwait(false);
        Assert.Equal(published.ClinicalSchemaJson, resolved.ClinicalSchemaJson);
        Assert.Equal(published.ContentHash, resolved.ContentHash);
        Assert.Contains("heart-rate", resolved.ClinicalSchemaJson, StringComparison.Ordinal);
        Assert.DoesNotContain("heart-rate-updated", resolved.ClinicalSchemaJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishDraft_FailsWhenComponentVersionIsMissing()
    {
        string formClinical = FormWithComponentRef("patient-section", "section.patient", "missing-component", "9.9.9");
        await CreateFormAsync("broken-form", "Broken form", formClinical, uiSchemaJson: null).ConfigureAwait(false);
        FormVersionDto draft = await GetEditableVersionAsync("broken-form").ConfigureAwait(false);

        using HttpResponseMessage response = await Client.PostAsJsonAsync(
            new Uri("/api/forms/broken-form/draft/submit-review", UriKind.Relative),
            new SubmitFormDraftForReviewRequest(draft.RowVersion)).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.Contains("COMPONENT_VERSION_NOT_FOUND", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishDraft_FailsWhenComponentVersionIsNotPinned()
    {
        await CreateAndPublishComponentAsync(
            "allergies",
            MinimalComponentClinicalSchema("allergy-list", "allergy.list"),
uiSchemaJson: null).ConfigureAwait(false);

        string formClinical = FormWithComponentRef("allergy-section", "section.allergies", "allergies", componentVersion: null);
        await CreateFormAsync("allergy-form", "Allergy form", formClinical, uiSchemaJson: null).ConfigureAwait(false);
        FormVersionDto draft = await GetEditableVersionAsync("allergy-form").ConfigureAwait(false);

        using HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/api/forms/allergy-form/draft/submit-review",
            new SubmitFormDraftForReviewRequest(draft.RowVersion)).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.Contains("COMPONENT_VERSION_REQUIRED", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishDraft_FailsOnCircularComponentReferences()
    {
        await CreateComponentAsync(
            "component-a",
            "Component A",
            ComponentRefClinicalSchema("ref-b", "component.b", "component-b", "1.0.0"),
uiSchemaJson: null).ConfigureAwait(false);

        await CreateComponentAsync(
            "component-b",
            "Component B",
            ComponentRefClinicalSchema("ref-a", "component.a", "component-a", "1.0.0"),
uiSchemaJson: null).ConfigureAwait(false);

        ComponentVersionDto draftA = await GetComponentDraftAsync("component-a").ConfigureAwait(false);
        await PublishComponentDraftAsync("component-a", draftA.RowVersion).ConfigureAwait(false);
        ComponentVersionDto draftB = await GetComponentDraftAsync("component-b").ConfigureAwait(false);
        await PublishComponentDraftAsync("component-b", draftB.RowVersion).ConfigureAwait(false);

        string formClinical = FormWithComponentRef("section-a", "section.a", "component-a", "1.0.0");
        await CreateFormAsync("circular-form", "Circular form", formClinical, uiSchemaJson: null).ConfigureAwait(false);
        FormVersionDto draft = await GetEditableVersionAsync("circular-form").ConfigureAwait(false);

        using HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/api/forms/circular-form/draft/submit-review",
            new SubmitFormDraftForReviewRequest(draft.RowVersion)).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.Contains("CIRCULAR_COMPONENT_REFERENCE", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishDraft_ProducesDeterministicSnapshotAndHash()
    {
        await CreateAndPublishComponentAsync(
            "consent-block",
            MinimalComponentClinicalSchema("consent-given", "consent.given"),
            MinimalUiSchema("consent-given", "Consent given")).ConfigureAwait(false);

        string formClinical = FormWithComponentRef("consent-section", "section.consent", "consent-block", "1.0.0");
        await CreateFormAsync("consent-form-a", "Consent form A", formClinical, uiSchemaJson: null).ConfigureAwait(false);
        await CreateFormAsync("consent-form-b", "Consent form B", formClinical, uiSchemaJson: null).ConfigureAwait(false);

        FormVersionDto draftA = await GetEditableVersionAsync("consent-form-a").ConfigureAwait(false);
        FormVersionDto draftB = await GetEditableVersionAsync("consent-form-b").ConfigureAwait(false);

        FormVersionDto publishedA = await PublishDraftAsync("consent-form-a", draftA.RowVersion).ConfigureAwait(false);
        FormVersionDto publishedB = await PublishDraftAsync("consent-form-b", draftB.RowVersion).ConfigureAwait(false);

        Assert.Equal(publishedA.ClinicalSchemaJson, publishedB.ClinicalSchemaJson);
        Assert.Equal(publishedA.UiSchemaJson, publishedB.UiSchemaJson);
        Assert.Equal(publishedA.DependencyMetadataJson, publishedB.DependencyMetadataJson);
        Assert.Equal(publishedA.ContentHash, publishedB.ContentHash);
    }

    private HttpClient Client { get; }

    private FormWebApplicationFactory Factory { get; } = new();

    private async Task CreateAndPublishComponentAsync(
        string code,
        string clinicalSchemaJson,
        string? uiSchemaJson)
    {
        await CreateComponentAsync(code, code, clinicalSchemaJson, uiSchemaJson).ConfigureAwait(false);
        ComponentVersionDto draft = await GetComponentDraftAsync(code).ConfigureAwait(false);
        await PublishComponentDraftAsync(code, draft.RowVersion).ConfigureAwait(false);
    }

    private async Task CreateComponentAsync(
        string code,
        string name,
        string clinicalSchemaJson,
        string? uiSchemaJson)
    {
        var request = new CreateComponentRequest(code, name, clinicalSchemaJson, uiSchemaJson);
        using HttpResponseMessage response = await Client.PostAsJsonAsync("/api/components", request).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.Created).ConfigureAwait(false);
    }

    private async Task CreateFormAsync(
        string code,
        string name,
        string clinicalSchemaJson,
        string? uiSchemaJson)
    {
        var request = new CreateFormRequest(code, name, clinicalSchemaJson, uiSchemaJson);
        using HttpResponseMessage response = await Client.PostAsJsonAsync("/api/forms", request).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.Created).ConfigureAwait(false);
    }

    private async Task<FormVersionDto> GetEditableVersionAsync(string code)
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri($"/api/forms/{code}/draft", UriKind.Relative)).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.OK).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<FormVersionDto>().ConfigureAwait(false))!;
    }

    private async Task<FormVersionDto> GetVersionAsync(string code, string version)
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri($"/api/forms/{code}/versions/{version}", UriKind.Relative)).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.OK).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<FormVersionDto>().ConfigureAwait(false))!;
    }

    private async Task<FormVersionDto> PublishDraftAsync(string code, uint rowVersion)
    {
        using HttpResponseMessage submitResponse = await Client.PostAsJsonAsync(
            $"/api/forms/{code}/draft/submit-review",
            new SubmitFormDraftForReviewRequest(rowVersion)).ConfigureAwait(false);
        await AssertStatusAsync(submitResponse, HttpStatusCode.OK).ConfigureAwait(false);
        FormVersionDto inReview = (await submitResponse.Content.ReadFromJsonAsync<FormVersionDto>().ConfigureAwait(false))!;

        using HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/api/forms/{code}/draft/publish",
            new PublishFormDraftRequest(inReview.RowVersion)).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.OK).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<FormVersionDto>().ConfigureAwait(false))!;
    }

    private async Task<ComponentVersionDto> GetComponentDraftAsync(string code)
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri($"/api/components/{code}/draft", UriKind.Relative)).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.OK).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<ComponentVersionDto>().ConfigureAwait(false))!;
    }

    private async Task UpdateComponentDraftAsync(
        string code,
        string clinicalSchemaJson,
        string? uiSchemaJson,
        uint rowVersion)
    {
        using HttpResponseMessage response = await Client.PutAsJsonAsync(
            $"/api/components/{code}/draft",
            new UpdateComponentDraftRequest(clinicalSchemaJson, uiSchemaJson, rowVersion)).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.OK).ConfigureAwait(false);
    }

    private async Task PublishComponentDraftAsync(string code, uint rowVersion)
    {
        using HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/api/components/{code}/draft/publish",
            new PublishComponentDraftRequest(rowVersion)).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.OK).ConfigureAwait(false);
    }

    private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode == expected)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.Fail(string.Create(CultureInfo.InvariantCulture, $"Expected {(int)expected} {expected}, got {(int)response.StatusCode} {response.StatusCode}. Body: {body}"));
    }

    private static string MinimalComponentClinicalSchema(string id, string code)
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

    private static string MinimalUiSchema(string fieldId, string label)
    {
        return JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0.0",
            clinicalSchemaVersion = "1.0.0",
            fields = new Dictionary<string, object>
(StringComparer.Ordinal)
            {
                [fieldId] = new
                {
                    label,
                    widget = "text-input",
                },
            },
        });
    }

    private static string FormWithComponentRef(
        string id,
        string code,
        string componentCode,
        string? componentVersion)
    {
        var field = new Dictionary<string, object?>
(StringComparer.Ordinal)
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
            fields = new Dictionary<string, object>
(StringComparer.Ordinal)
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
