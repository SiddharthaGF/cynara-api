using System.Net;
using System.Text.Json;

using Cynara.Api.Tests.Support;

namespace Cynara.Api.Tests;

[Collection(PostgresFixtureDefinition.Name)]
public sealed class FormResponseValidationTests : IDisposable
{
    public FormResponseValidationTests(PostgreSqlDatabaseFixture database)
    {
        Factory = new FormWebApplicationFactory(database.Settings);
        Client = Factory.CreateClient();
        Client.AcceptJsonApi();
        Client.DefaultRequestHeaders.Add("X-Actor-Id", "test-clinician");
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
    public async Task CompleteResponse_RejectsCrossFieldRuleViolations()
    {
        using JsonDocument response = await CreatePublishedBpResponseAsync()
            .ConfigureAwait(false);
        string id = JsonApiClient.RequireId(response);

        using JsonDocument updated = await Api.PatchResourceAsync(
            "formResponses",
            id,
            new
            {
                answersJson = /*lang=json,strict*/ """
                    {
                      "vital.bp.systolic": 120,
                      "vital.bp.diastolic": 130
                    }
                    """,
                rowVersion = JsonApiClient.AttrUInt(response, "rowVersion"),
            }).ConfigureAwait(false);

        using HttpResponseMessage completeResponse = await Api.PostActionRawAsync(
            $"/api/formResponses/{id}/complete",
            new { rowVersion = JsonApiClient.AttrUInt(updated, "rowVersion") })
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, completeResponse.StatusCode);
        string body = await completeResponse.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        JsonElement errors = document.RootElement.GetProperty("errors");
        Assert.Contains(
            errors.EnumerateArray(),
            error => string.Equals(
                error.GetProperty("title").GetString(),
                "Validation failed",
                StringComparison.Ordinal));
        Assert.Contains(
            errors.EnumerateArray(),
            error => string.Equals(
                error.GetProperty("code").GetString(),
                "BP_SYSTOLIC_GT_DIASTOLIC",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task CompleteResponse_RejectsMissingRequiredFields()
    {
        using JsonDocument response = await CreatePublishedRequiredFieldResponseAsync()
            .ConfigureAwait(false);
        string id = JsonApiClient.RequireId(response);

        using HttpResponseMessage completeResponse = await Api.PostActionRawAsync(
            $"/api/formResponses/{id}/complete",
            new { rowVersion = JsonApiClient.AttrUInt(response, "rowVersion") })
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, completeResponse.StatusCode);
        string body = await completeResponse.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        JsonElement errors = document.RootElement.GetProperty("errors");
        Assert.Contains(
            errors.EnumerateArray(),
            error => string.Equals(
                    error.GetProperty("code").GetString(),
                    "REQUIRED_FIELD_MISSING",
                    StringComparison.Ordinal)
                && (error.GetProperty("source").GetProperty("pointer").GetString()
                    ?? string.Empty)
                    .Contains("/fields/0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpdateResponse_RejectsUnknownFieldTampering()
    {
        using JsonDocument response = await CreatePublishedRequiredFieldResponseAsync()
            .ConfigureAwait(false);
        string id = JsonApiClient.RequireId(response);

        using var content = JsonApiClient.CreateJsonApiContent(new
        {
            data = new
            {
                type = "formResponses",
                id,
                attributes = new
                {
                    answersJson = /*lang=json,strict*/ """
                        {
                          "patient.name": "Ada",
                          "secret.backdoor": "tampered"
                        }
                        """,
                    rowVersion = JsonApiClient.AttrUInt(response, "rowVersion"),
                },
            },
        });
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            new Uri($"/api/formResponses/{id}", UriKind.Relative))
        {
            Content = content,
        };
        using HttpResponseMessage updateResponse = await Client.SendAsync(request)
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, updateResponse.StatusCode);
        string body = await updateResponse.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains("UNKNOWN_FIELD", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateResponse_OverwritesCalculatedFieldsFromServerRules()
    {
        using JsonDocument response = await CreatePublishedBmiResponseAsync()
            .ConfigureAwait(false);
        string id = JsonApiClient.RequireId(response);

        using JsonDocument updated = await Api.PatchResourceAsync(
            "formResponses",
            id,
            new
            {
                answersJson = /*lang=json,strict*/ """
                    {
                      "body.weight.kg": 70,
                      "body.height.m": 1.75,
                      "body.bmi": 999
                    }
                    """,
                rowVersion = JsonApiClient.AttrUInt(response, "rowVersion"),
            }).ConfigureAwait(false);

        using var answers = JsonDocument.Parse(
            JsonApiClient.AttrString(updated, "answersJson")!);
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

        var validator = new Application.Forms.FormResponseValidator(
            new Application.Forms.FormRuleEngine());
        Application.Forms.FormResponseValidationResult result = validator.Validate(
            clinical,
            ui,
            rulesSchemaJson: null,
            /*lang=json,strict*/
            """{"form.notes":"visible","form.secret":"tampered"}""",
            Application.Forms.FormResponseValidationMode.Draft);

        Assert.Contains(
            result.Errors,
            error => string.Equals(error.Code, "HIDDEN_FIELD_VALUE", StringComparison.Ordinal)
                && string.Equals(error.Path, "/fields/1", StringComparison.Ordinal));
    }

    private HttpClient Client { get; }

    private JsonApiClient Api { get; }

    private JsonApiWorkflow Workflow { get; }

    private FormWebApplicationFactory Factory { get; }

    private async Task<JsonDocument> CreatePublishedBpResponseAsync()
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

        return await CreatePublishedResponseAsync("bp-validation", clinical, rules)
            .ConfigureAwait(false);
    }

    private async Task<JsonDocument> CreatePublishedRequiredFieldResponseAsync()
    {
        const string clinical = /*lang=json,strict*/ """
            {
              "schemaVersion": "1.0.0",
              "fields": [
                { "id": "patient-name", "code": "patient.name", "type": "text", "required": true }
              ]
            }
            """;

        return await CreatePublishedResponseAsync(
            "required-field",
            clinical,
            rulesSchemaJson: null).ConfigureAwait(false);
    }

    private async Task<JsonDocument> CreatePublishedBmiResponseAsync()
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

        return await CreatePublishedResponseAsync("bmi-response", clinical, rules)
            .ConfigureAwait(false);
    }

    private async Task<JsonDocument> CreatePublishedResponseAsync(
        string code,
        string clinical,
        string? rulesSchemaJson)
    {
        (_, string versionId) = await Workflow.PublishFormAsync(
            code,
            code,
            clinical,
            uiSchemaJson: null,
            rulesSchemaJson).ConfigureAwait(false);
        return await Workflow.CreateResponseAsync(versionId).ConfigureAwait(false);
    }
}
