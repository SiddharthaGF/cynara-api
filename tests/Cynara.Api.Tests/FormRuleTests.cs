using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Cynara.Application;
using Cynara.Application.Forms;

using Xunit;

namespace Cynara.Api.Tests;

public class FormRuleTests
{
    private static readonly string FixtureRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "cynara", "tests", "fixtures", "rules"));

    [Theory]
    [InlineData("evaluate-visibility.json")]
    [InlineData("evaluate-calculation.json")]
    [InlineData("evaluate-calculation-incomplete.json")]
    [InlineData("evaluate-calculation-nonfinite.json")]
    [InlineData("evaluate-bp-validation.json")]
    public void EvaluateRules_MatchesSharedFixtures(string fixtureName)
    {
        using JsonDocument fixture = LoadFixture(fixtureName);
        string clinical = fixture.RootElement.GetProperty("clinical").GetRawText();
        string rules = fixture.RootElement.GetProperty("rules").GetRawText();
        Dictionary<string, object?> values = ParseValues(fixture.RootElement.GetProperty("values"));
        JsonElement expected = fixture.RootElement.GetProperty("expected");

        var engine = new FormRuleEngine();
        FormRuleEvaluationResult result = engine.Evaluate(clinical, rules, values);

        if (expected.TryGetProperty("visibility", out JsonElement visibility))
        {
            foreach (JsonProperty property in visibility.EnumerateObject())
            {
                Assert.Equal(property.Value.GetBoolean(), result.Visibility[property.Name]);
            }
        }

        if (expected.TryGetProperty("calculatedValues", out JsonElement calculatedValues))
        {
            foreach (JsonProperty property in calculatedValues.EnumerateObject())
            {
                Assert.True(result.CalculatedValues.ContainsKey(property.Name));
                object? actual = result.CalculatedValues[property.Name];
                if (property.Value.ValueKind == JsonValueKind.Null)
                {
                    Assert.Null(actual);
                    continue;
                }

                Assert.Equal(property.Value.GetDouble(), Convert.ToDouble(actual));
            }
        }

        if (expected.TryGetProperty("validationErrors", out JsonElement validationErrors))
        {
            Assert.Equal(validationErrors.GetArrayLength(), result.ValidationErrors.Count);
            for (int index = 0; index < validationErrors.GetArrayLength(); index++)
            {
                JsonElement expectedError = validationErrors[index];
                RuleValidationError actualError = result.ValidationErrors[index];
                Assert.Equal(expectedError.GetProperty("code").GetString(), actualError.Code);
                Assert.Equal(expectedError.GetProperty("message").GetString(), actualError.Message);
            }
        }
    }

    [Fact]
    public void ValidateDependencies_RejectsCyclicCalculations()
    {
        using JsonDocument fixture = LoadInvalidFixture("rules-cyclic-calculation.json");
        string clinical = fixture.RootElement.GetProperty("clinical").GetRawText();
        string rules = fixture.RootElement.GetProperty("rules").GetRawText();

        ValidationException exception = Assert.Throws<ValidationException>(
            () => FormRuleAnalyzer.ValidateDependencies(clinical, rules));

        Assert.Contains("RULE_CYCLIC_DEPENDENCY", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateForm_RejectsCalculateOnWritableField()
    {
        await using FormWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();

        string clinical = MinimalClinicalSchema();
        string rules = /*lang=json,strict*/ """
            {
              "schemaVersion": "1.0.0",
              "clinicalSchemaVersion": "1.0.0",
              "fields": {
                "weight-kg": {
                  "calculate": { "ref": "body.weight.kg" }
                }
              }
            }
            """;

        var createRequest = new CreateFormRequest("rules-form", "Rules form", clinical, null, rules);
        using HttpResponseMessage createResponse = await client.PostAsJsonAsync("/api/forms", createRequest);

        Assert.Equal(HttpStatusCode.BadRequest, createResponse.StatusCode);
        string body = await createResponse.Content.ReadAsStringAsync();
        Assert.Contains("RULE_CALCULATE_NOT_READONLY", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishDraft_IncludesRulesInSnapshotAndDependencyMetadata()
    {
        await using FormWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();

        string clinical = /*lang=json,strict*/ """
            {
              "schemaVersion": "1.0.0",
              "fields": [
                { "id": "weight-kg", "code": "body.weight.kg", "type": "number" },
                { "id": "height-m", "code": "body.height.m", "type": "number" },
                { "id": "bmi", "code": "body.bmi", "type": "number", "readOnly": true }
              ]
            }
            """;
        string rules = /*lang=json,strict*/ """
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

        var createRequest = new CreateFormRequest("bmi-form", "BMI form", clinical, null, rules);
        using HttpResponseMessage createResponse = await client.PostAsJsonAsync("/api/forms", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        FormVersionDto draft = await createResponse.Content.ReadFromJsonAsync<FormVersionDto>()
            ?? throw new InvalidOperationException("Missing draft response.");

        using HttpResponseMessage submitResponse = await client.PostAsJsonAsync(
            "/api/forms/bmi-form/draft/submit-review",
            new SubmitFormDraftForReviewRequest(draft.RowVersion));
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        FormVersionDto inReview = (await submitResponse.Content.ReadFromJsonAsync<FormVersionDto>())!;
        using HttpResponseMessage publishResponse = await client.PostAsJsonAsync(
            "/api/forms/bmi-form/draft/publish",
            new PublishFormDraftRequest(inReview.RowVersion));
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);

        FormVersionDto published = (await publishResponse.Content.ReadFromJsonAsync<FormVersionDto>())!;
        Assert.NotNull(published.RulesSchemaJson);
        Assert.Contains("body.weight.kg", published.RulesSchemaJson, StringComparison.Ordinal);
        Assert.NotNull(published.DependencyMetadataJson);
        Assert.Contains("evaluationOrder", published.DependencyMetadataJson, StringComparison.Ordinal);
        Assert.Contains("bmi", published.DependencyMetadataJson, StringComparison.Ordinal);
    }

    private static JsonDocument LoadFixture(string fixtureName)
    {
        string path = Path.Combine(FixtureRoot, fixtureName);
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static JsonDocument LoadInvalidFixture(string fixtureName)
    {
        string path = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "cynara", "tests", "fixtures", "invalid", fixtureName));
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static Dictionary<string, object?> ParseValues(JsonElement values)
    {
        var parsed = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (JsonProperty property in values.EnumerateObject())
        {
            parsed[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number when property.Value.TryGetInt32(out int integer) => integer,
                JsonValueKind.Number => property.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                JsonValueKind.Undefined => throw new NotImplementedException(),
                JsonValueKind.Object => throw new NotImplementedException(),
                JsonValueKind.Array => throw new NotImplementedException(),
                _ => property.Value.GetRawText(),
            };
        }

        return parsed;
    }

    private static string MinimalClinicalSchema()
    {
        return /*lang=json,strict*/ """
            {
              "schemaVersion": "1.0.0",
              "fields": [
                { "id": "weight-kg", "code": "body.weight.kg", "type": "number" }
              ]
            }
            """;
    }
}
