using System.Globalization;
using System.Net;
using System.Text.Json;

using Cynara.Api.Tests.Support;
using Cynara.Application;
using Cynara.Application.Forms;

namespace Cynara.Api.Tests;

[Collection(PostgresFixtureDefinition.Name)]
public sealed class FormRuleTests
{
    private readonly PostgreSqlDatabaseFixture database;

    public FormRuleTests(PostgreSqlDatabaseFixture database)
    {
        this.database = database;
    }

    private static readonly string FixtureRoot = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "rules");

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

                Assert.Equal(
                    property.Value.GetDouble(),
                    Convert.ToDouble(actual, CultureInfo.InvariantCulture));
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
        await using FormWebApplicationFactory factory = new(database.Settings);
        using HttpClient client = factory.CreateClient();
        client.AcceptJsonApi();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            factory.BootstrapOptions.BootstrapCode ?? "default");

        string clinical = MinimalClinicalSchema();
        const string rules = /*lang=json,strict*/ """
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

        using var content = JsonApiClient.CreateJsonApiContent(new
        {
            data = new
            {
                type = "formDefinitions",
                attributes = new
                {
                    code = "rules-form",
                    name = "Rules form",
                    initialClinicalSchemaJson = clinical,
                    initialRulesSchemaJson = rules,
                },
            },
        });
        using HttpResponseMessage createResponse = await client.PostAsync(
            new Uri("/api/formDefinitions", UriKind.Relative),
            content).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, createResponse.StatusCode);
        string body = await createResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.Contains("RULE_CALCULATE_NOT_READONLY", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishDraft_IncludesRulesInSnapshotAndDependencyMetadata()
    {
        await using FormWebApplicationFactory factory = new(database.Settings);
        using HttpClient client = factory.CreateClient();
        client.AcceptJsonApi();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            factory.BootstrapOptions.BootstrapCode ?? "default");
        var api = new JsonApiClient(client);
        var workflow = new JsonApiWorkflow(api, client);

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

        string definitionId = await workflow.CreateFormDefinitionAsync(
            "bmi-form",
            "BMI form",
            clinical,
            uiSchemaJson: null,
            rules).ConfigureAwait(false);
        string draftId = await workflow.GetFormDraftIdAsync(definitionId)
            .ConfigureAwait(false);
        using JsonDocument published = await workflow.SubmitAndPublishFormAsync(draftId)
            .ConfigureAwait(false);

        string? rulesSchemaJson = JsonApiClient.AttrString(published, "rulesSchemaJson");
        Assert.NotNull(rulesSchemaJson);
        Assert.Contains("body.weight.kg", rulesSchemaJson, StringComparison.Ordinal);
        string? dependencyMetadataJson = JsonApiClient.AttrString(
            published,
            "dependencyMetadataJson");
        Assert.NotNull(dependencyMetadataJson);
        Assert.Contains("evaluationOrder", dependencyMetadataJson, StringComparison.Ordinal);
        Assert.Contains("bmi", dependencyMetadataJson, StringComparison.Ordinal);
    }

    private static JsonDocument LoadFixture(string fixtureName)
    {
        string path = Path.Combine(FixtureRoot, fixtureName);
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static JsonDocument LoadInvalidFixture(string fixtureName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "invalid", fixtureName);
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
                JsonValueKind.Undefined => throw new NotSupportedException(
                    "Undefined JSON values are not supported."),
                JsonValueKind.Object => throw new NotSupportedException(
                    "Nested JSON objects are not supported."),
                JsonValueKind.Array => throw new NotSupportedException(
                    "JSON arrays are not supported."),
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
