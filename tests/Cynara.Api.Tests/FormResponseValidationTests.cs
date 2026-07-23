using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Cynara.Application.Forms;

using Xunit;

namespace Cynara.Api.Tests;

public sealed class FormResponseValidationTests
{
    public FormResponseValidationTests()
    {
        Client = Factory.CreateClient();
        Client.DefaultRequestHeaders.Add("X-Actor-Id", "test-clinician");
    }

    private HttpClient Client { get; }

    private FormWebApplicationFactory Factory { get; } = new();

    [Fact]
    public async Task CompleteResponse_RejectsCrossFieldRuleViolations()
    {
        FormResponseDto response = await CreatePublishedBpResponseAsync().ConfigureAwait(false);

        var updateRequest = new UpdateFormResponseRequest(
                                 /*lang=json,strict*/
                                 """
            {
              "vital.bp.systolic": 120,
              "vital.bp.diastolic": 130
            }
            """,
                                 response.RowVersion);
        using HttpResponseMessage updateResponse = await Client.PutAsJsonAsync(
            $"/api/responses/{response.Id}",
            updateRequest).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        FormResponseDto updated = (await updateResponse.Content.ReadFromJsonAsync<FormResponseDto>().ConfigureAwait(false))!;
        using HttpResponseMessage completeResponse = await Client.PostAsJsonAsync(
            $"/api/responses/{response.Id}/complete",
            new CompleteFormResponseRequest(updated.RowVersion)).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, completeResponse.StatusCode);
        string body = await completeResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("Validation failed", document.RootElement.GetProperty("title").GetString());
        JsonElement errors = document.RootElement.GetProperty("errors");
        Assert.Contains(
            errors.EnumerateArray(),
            error => string.Equals(error.GetProperty("code").GetString(), "BP_SYSTOLIC_GT_DIASTOLIC", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CompleteResponse_RejectsMissingRequiredFields()
    {
        FormResponseDto response = await CreatePublishedRequiredFieldResponseAsync().ConfigureAwait(false);

        using HttpResponseMessage completeResponse = await Client.PostAsJsonAsync(
            $"/api/responses/{response.Id}/complete",
            new CompleteFormResponseRequest(response.RowVersion)).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, completeResponse.StatusCode);
        string body = await completeResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        JsonElement errors = document.RootElement.GetProperty("errors");
        Assert.Contains(
            errors.EnumerateArray(),
            error => string.Equals(error.GetProperty("code").GetString(), "REQUIRED_FIELD_MISSING"
, StringComparison.Ordinal) && string.Equals(error.GetProperty("path").GetString(), "/fields/0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpdateResponse_RejectsUnknownFieldTampering()
    {
        FormResponseDto response = await CreatePublishedRequiredFieldResponseAsync().ConfigureAwait(false);

        var updateRequest = new UpdateFormResponseRequest(
                                 /*lang=json,strict*/
                                 """
            {
              "patient.name": "Ada",
              "secret.backdoor": "tampered"
            }
            """,
                                 response.RowVersion);
        using HttpResponseMessage updateResponse = await Client.PutAsJsonAsync(
            $"/api/responses/{response.Id}",
            updateRequest).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, updateResponse.StatusCode);
        string body = await updateResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.Contains("UNKNOWN_FIELD", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateResponse_OverwritesCalculatedFieldsFromServerRules()
    {
        FormResponseDto response = await CreatePublishedBmiResponseAsync().ConfigureAwait(false);

        var updateRequest = new UpdateFormResponseRequest(
                                 /*lang=json,strict*/
                                 """
            {
              "body.weight.kg": 70,
              "body.height.m": 1.75,
              "body.bmi": 999
            }
            """,
                                 response.RowVersion);
        using HttpResponseMessage updateResponse = await Client.PutAsJsonAsync(
            $"/api/responses/{response.Id}",
            updateRequest).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        FormResponseDto updated = (await updateResponse.Content.ReadFromJsonAsync<FormResponseDto>().ConfigureAwait(false))!;
        using var answers = JsonDocument.Parse(updated.AnswersJson);
        double bmi = answers.RootElement.GetProperty("body.bmi").GetDouble();
        Assert.InRange(bmi, 22.8, 22.9);
    }

    [Fact]
    public void ValidateResponse_RejectsHiddenFieldTampering()
    {
        const string clinical = /*lang=json,strict*/ """
            {
              "schemaVersion": "1.0.0",
              "fields": [
                { "id": "notes", "code": "form.notes", "type": "text" },
                { "id": "secret", "code": "form.secret", "type": "text" }
              ]
            }
            """;
        const string ui = /*lang=json,strict*/ """
            {
              "schemaVersion": "1.0.0",
              "clinicalSchemaVersion": "1.0.0",
              "fields": {
                "secret": { "hidden": true }
              }
            }
            """;

        var validator = new FormResponseValidator(new FormRuleEngine());
        FormResponseValidationResult result = validator.Validate(
            clinical,
            ui,
            rulesSchemaJson: null,
            /*lang=json,strict*/
            """{"form.notes":"visible","form.secret":"tampered"}""",
            FormResponseValidationMode.Draft);

        Assert.Contains(
            result.Errors,
            error => string.Equals(error.Code, "HIDDEN_FIELD_VALUE", StringComparison.Ordinal) && string.Equals(error.Path, "/fields/1", StringComparison.Ordinal));
    }

    private async Task<FormResponseDto> CreatePublishedBpResponseAsync()
    {
        const string clinical = /*lang=json,strict*/ """
            {
              "schemaVersion": "1.0.0",
              "fields": [
                { "id": "systolic", "code": "vital.bp.systolic", "type": "integer" },
                { "id": "diastolic", "code": "vital.bp.diastolic", "type": "integer" }
              ]
            }
            """;
        const string rules = /*lang=json,strict*/ """
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

        return await CreatePublishedResponseAsync("bp-validation", clinical, rules).ConfigureAwait(false);
    }

    private async Task<FormResponseDto> CreatePublishedRequiredFieldResponseAsync()
    {
        const string clinical = /*lang=json,strict*/ """
            {
              "schemaVersion": "1.0.0",
              "fields": [
                { "id": "patient-name", "code": "patient.name", "type": "text", "required": true }
              ]
            }
            """;

        return await CreatePublishedResponseAsync("required-field", clinical, rulesSchemaJson: null).ConfigureAwait(false);
    }

    private async Task<FormResponseDto> CreatePublishedBmiResponseAsync()
    {
        const string clinical = /*lang=json,strict*/ """
            {
              "schemaVersion": "1.0.0",
              "fields": [
                { "id": "weight-kg", "code": "body.weight.kg", "type": "number" },
                { "id": "height-m", "code": "body.height.m", "type": "number" },
                { "id": "bmi", "code": "body.bmi", "type": "number", "readOnly": true }
              ]
            }
            """;
        const string rules = /*lang=json,strict*/ """
            {
              "schemaVersion": "1.0.0",
              "clinicalSchemaVersion": "1.0.0",
              "fields": {
                "bmi": {
                  "calculate": {
                    "op": "div",
                    "args": [
                      { "ref": "body.weight.kg" },
                      { "op": "mul", "args": [{ "ref": "body.height.m" }, { "ref": "body.height.m" }] }
                    ]
                  }
                }
              }
            }
            """;

        return await CreatePublishedResponseAsync("bmi-response", clinical, rules).ConfigureAwait(false);
    }

    private async Task<FormResponseDto> CreatePublishedResponseAsync(
        string code,
        string clinical,
        string? rulesSchemaJson)
    {
        var createFormRequest = new CreateFormRequest(code, code, clinical, UiSchemaJson: null, rulesSchemaJson);
        using HttpResponseMessage createFormResponse = await Client.PostAsJsonAsync("/api/forms", createFormRequest).ConfigureAwait(false);
        if (createFormResponse.StatusCode != HttpStatusCode.Created)
        {
            string body = await createFormResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.Fail(
                string.Create(CultureInfo.InvariantCulture, $"Expected form creation to succeed, got {(int)createFormResponse.StatusCode} {createFormResponse.StatusCode}. Body: {body}"));
        }

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
}
