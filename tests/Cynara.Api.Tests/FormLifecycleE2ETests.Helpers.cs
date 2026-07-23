using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Cynara.Application.Audit;
using Cynara.Application.Components;
using Cynara.Application.Forms;

namespace Cynara.Api.Tests;

public sealed partial class FormLifecycleE2ETests
{
    private FormWebApplicationFactory Factory { get; } = new();

    private async Task CreateAndPublishComponentAsync(string code, string clinical, string ui)
    {
        await CreateComponentAsync(code, code, clinical, ui).ConfigureAwait(false);
        ComponentVersionDto draft = await GetComponentDraftAsync(code).ConfigureAwait(false);
        await PublishComponentDraftAsync(code, draft.RowVersion).ConfigureAwait(false);
    }

    private async Task CreateComponentAsync(string code, string name, string clinical, string ui)
    {
        var request = new CreateComponentRequest(code, name, clinical, ui);
        using HttpResponseMessage response = await Client.PostAsJsonAsync("/api/components", request).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.Created).ConfigureAwait(false);
    }

    private async Task CreateFormAsync(
        string code,
        string name,
        string clinical,
        string? ui,
        string? rules = null)
    {
        var request = new CreateFormRequest(code, name, clinical, ui, rules);
        using HttpResponseMessage response = await Client.PostAsJsonAsync("/api/forms", request).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.Created).ConfigureAwait(false);
    }

    private async Task<FormVersionDto> UpdateDraftAsync(
        string code,
        string clinical,
        string? ui,
        string? rules,
        uint rowVersion)
    {
        var request = new UpdateFormDraftRequest(clinical, ui, rules, rowVersion);
        using HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/forms/{code}/draft", request).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.OK).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<FormVersionDto>().ConfigureAwait(false))!;
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

    private async Task<FormVersionDto> SubmitForReviewAsync(string code, uint rowVersion)
    {
        using HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/api/forms/{code}/draft/submit-review",
            new SubmitFormDraftForReviewRequest(rowVersion)).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.OK).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<FormVersionDto>().ConfigureAwait(false))!;
    }

    private async Task<FormVersionDto> SubmitAndPublishAsync(string code, uint draftRowVersion)
    {
        FormVersionDto inReview = await SubmitForReviewAsync(code, draftRowVersion).ConfigureAwait(false);
        using HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/api/forms/{code}/draft/publish",
            new PublishFormDraftRequest(inReview.RowVersion)).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.OK).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<FormVersionDto>().ConfigureAwait(false))!;
    }

    private async Task<FormResponseDto> CreateResponseAsync(string code, string version)
    {
        using HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/api/forms/{code}/versions/{version}/responses",
            new CreateFormResponseRequest()).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.Created).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<FormResponseDto>().ConfigureAwait(false))!;
    }

    private async Task<FormResponseDto> UpdateResponseAsync(Guid id, string answersJson, uint rowVersion)
    {
        var request = new UpdateFormResponseRequest(answersJson, rowVersion);
        using HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/responses/{id}", request).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.OK).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<FormResponseDto>().ConfigureAwait(false))!;
    }

    private async Task<FormResponseDto> CompleteResponseAsync(Guid id, uint rowVersion)
    {
        using HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/api/responses/{id}/complete",
            new CompleteFormResponseRequest(rowVersion)).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.OK).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<FormResponseDto>().ConfigureAwait(false))!;
    }

    private async Task<FormResponseDto> GetResponseAsync(Guid id, bool includeDeleted = false)
    {
        using HttpResponseMessage response = await Client.GetAsync(
            new Uri($"/api/responses/{id}?includeDeleted={includeDeleted.ToString().ToUpperInvariant()}", UriKind.Relative)).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.OK).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<FormResponseDto>().ConfigureAwait(false))!;
    }

    private async Task<FormResponseDto> SoftDeleteResponseAsync(Guid id, string reason)
    {
        using HttpResponseMessage response = await Client.DeleteAsync(
            new Uri($"/api/responses/{id}?reason={Uri.EscapeDataString(reason)}", UriKind.Relative)).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.NoContent).ConfigureAwait(false);
        return await GetResponseAsync(id, includeDeleted: true).ConfigureAwait(false);
    }

    private async Task<FormResponseRevisionDto> GetResponseRevisionAsync(Guid id, uint revisionNumber)
    {
        using HttpResponseMessage response = await Client.GetAsync(
            new Uri($"/api/responses/{id}/revisions/{revisionNumber}", UriKind.Relative)).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.OK).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<FormResponseRevisionDto>().ConfigureAwait(false))!;
    }

    private async Task<List<AuditEventDto>> ListAuditEventsAsync(string resourceType, Guid resourceId)
    {
        using HttpResponseMessage response = await Client.GetAsync(
            new Uri($"/api/audit/events?resourceType={resourceType}&resourceId={resourceId}", UriKind.Relative)).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.OK).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<List<AuditEventDto>>().ConfigureAwait(false))!;
    }

    private async Task<ComponentVersionDto> GetComponentDraftAsync(string code)
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri($"/api/components/{code}/draft", UriKind.Relative)).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.OK).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<ComponentVersionDto>().ConfigureAwait(false))!;
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

    private static string MinimalComponentClinicalSchema(string id, string code)
    {
        return MinimalClinicalSchema(id, code);
    }

    private static string MinimalComponentUiSchema(string fieldId, string label)
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

    private static string BpClinicalSchema()
    {
        return /*lang=json,strict*/ """
            {
              "schemaVersion": "1.0.0",
              "fields": [
                { "id": "systolic", "code": "vital.bp.systolic", "type": "integer" },
                { "id": "diastolic", "code": "vital.bp.diastolic", "type": "integer" }
              ]
            }
            """;
    }

    private static string BpValidationRulesSchema()
    {
        return /*lang=json,strict*/ """
            {
              "schemaVersion": "1.0.0",
              "clinicalSchemaVersion": "1.0.0",
              "fields": {},
              "validations": [
                {
                  "code": "BP_SYSTOLIC_GT_DIASTOLIC",
                  "message": "Systolic must be greater than diastolic",
                  "when": {
                    "op": "and",
                    "args": [
                      { "op": "not", "args": [{ "op": "empty", "args": [{ "ref": "vital.bp.systolic" }] }] },
                      { "op": "not", "args": [{ "op": "empty", "args": [{ "ref": "vital.bp.diastolic" }] }] }
                    ]
                  },
                  "assert": {
                    "op": "gt",
                    "args": [
                      { "ref": "vital.bp.systolic" },
                      { "ref": "vital.bp.diastolic" }
                    ]
                  }
                }
              ]
            }
            """;
    }

    private static string FormWithComponentRefAndVitals(string sectionId, string sectionCode)
    {
        return JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0.0",
            fields = new object[]
            {
                new
                {
                    id = sectionId,
                    code = sectionCode,
                    type = "component-ref",
                    componentCode = "patient-demographics",
                    componentVersion = "1.0.0",
                },
                new
                {
                    id = "systolic",
                    code = "vital.bp.systolic",
                    type = "integer",
                },
                new
                {
                    id = "diastolic",
                    code = "vital.bp.diastolic",
                    type = "integer",
                },
            },
        });
    }
}
