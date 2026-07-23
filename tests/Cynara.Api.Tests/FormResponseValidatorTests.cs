using System.Text.Json;

using Cynara.Application.Forms;

namespace Cynara.Api.Tests;

public sealed class FormResponseValidatorTests
{
    private readonly FormResponseValidator validator = new(new FormRuleEngine());

    [Fact]
    public void ValidateComplete_RejectsMissingRequiredField()
    {
        const string clinical = /*lang=json,strict*/ """
            {
              "schemaVersion": "1.0.0",
              "fields": [
                { "id": "patient-name", "code": "patient.name", "type": "text", "required": true }
              ]
            }
            """;

        FormResponseValidationResult result = validator.Validate(
            clinical,
            uiSchemaJson: null,
            rulesSchemaJson: null,
            answersJson: "{}",
            FormResponseValidationMode.Complete);

        Assert.Contains(
            result.Errors,
            error => string.Equals(error.Code, "REQUIRED_FIELD_MISSING", StringComparison.Ordinal) && string.Equals(error.Path, "/fields/0", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateDraft_OverwritesTamperedCalculatedField()
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

        FormResponseValidationResult result = validator.Validate(
            clinical,
            uiSchemaJson: null,
            rules,
            /*lang=json,strict*/
            """
            {
              "body.weight.kg": 70,
              "body.height.m": 1.75,
              "body.bmi": 999
            }
            """,
            FormResponseValidationMode.Draft);

        Assert.Empty(result.Errors);
        using var answers = JsonDocument.Parse(result.NormalizedAnswersJson);
        double bmi = answers.RootElement.GetProperty("body.bmi").GetDouble();
        Assert.InRange(bmi, 22.8, 22.9);
    }
}
