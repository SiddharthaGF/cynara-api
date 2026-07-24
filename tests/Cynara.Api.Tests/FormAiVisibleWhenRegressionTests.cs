using System.Text.Json.Nodes;

using Cynara.Application.Modules.FormAi;
using Cynara.Infrastructure.Schemas;

namespace Cynara.Api.Tests;

public sealed class FormAiVisibleWhenRegressionTests
{
    private const string Clinical = /*lang=json,strict*/ """
        {
          "schemaVersion": "1.0.0",
          "fields": [
            {
              "id": "bp-systolic",
              "code": "vital.bp.systolic",
              "type": "integer",
              "required": true,
              "minimum": 50,
              "maximum": 300
            },
            {
              "id": "bp-diastolic",
              "code": "vital.bp.diastolic",
              "type": "integer",
              "required": true,
              "minimum": 30,
              "maximum": 200
            },
            {
              "id": "heart-rate",
              "code": "vital.heart.rate",
              "type": "integer",
              "required": true,
              "minimum": 20,
              "maximum": 300
            },
            {
              "id": "oxygen-saturation",
              "code": "vital.spo2",
              "type": "number",
              "required": true,
              "minimum": 0,
              "maximum": 100
            },
            {
              "id": "pain-scale",
              "code": "vital.pain.scale",
              "type": "integer",
              "required": true,
              "minimum": 0,
              "maximum": 10
            }
          ]
        }
        """;

    private const string Ui = /*lang=json,strict*/ """
        {
          "schemaVersion": "1.0.0",
          "clinicalSchemaVersion": "1.0.0",
          "fields": {
            "bp-systolic": {
              "label": "Tensión arterial sistólica (mmHg)",
              "widget": "integer-input",
              "width": "half"
            },
            "bp-diastolic": {
              "label": "Tensión arterial diastólica (mmHg)",
              "widget": "integer-input",
              "width": "half"
            },
            "heart-rate": {
              "label": "Frecuencia cardíaca (lpm)",
              "widget": "integer-input",
              "width": "half"
            },
            "oxygen-saturation": {
              "label": "Saturación de oxígeno SpO2 (%)",
              "widget": "number-input",
              "width": "half"
            },
            "pain-scale": {
              "label": "Escala de dolor (0-10)",
              "widget": "integer-input",
              "width": "half"
            }
          },
          "layout": [
            {
              "type": "section",
              "title": "Signos vitales",
              "children": [
                { "type": "field", "fieldId": "bp-systolic" },
                { "type": "field", "fieldId": "bp-diastolic" },
                { "type": "field", "fieldId": "heart-rate" },
                { "type": "field", "fieldId": "oxygen-saturation" },
                { "type": "field", "fieldId": "pain-scale" }
              ]
            }
          ]
        }
        """;

    private const string RulesWithBpValidation = /*lang=json,strict*/ """
        {
          "schemaVersion": "1.0.0",
          "clinicalSchemaVersion": "1.0.0",
          "fields": {},
          "validations": [
            {
              "code": "BP_SYSTOLIC_GT_DIASTOLIC",
              "message": "La tensión sistólica debe ser mayor que la diastólica.",
              "assert": {
                "op": "gt",
                "args": [
                  { "ref": "vital.bp.systolic" },
                  { "ref": "vital.bp.diastolic" }
                ]
              },
              "when": {
                "op": "and",
                "args": [
                  {
                    "op": "not",
                    "args": [
                      {
                        "op": "empty",
                        "args": [{ "ref": "vital.bp.systolic" }]
                      }
                    ]
                  },
                  {
                    "op": "not",
                    "args": [
                      {
                        "op": "empty",
                        "args": [{ "ref": "vital.bp.diastolic" }]
                      }
                    ]
                  }
                ]
              }
            }
          ]
        }
        """;

    private const string VisibleWhenPatch = /*lang=json,strict*/ """
        {
          "upsertRulesFields": {
            "oxygen-saturation": {
              "visibleWhen": {
                "op": "gte",
                "args": [
                  { "ref": "vital.heart.rate" },
                  { "lit": 100 }
                ]
              }
            }
          }
        }
        """;

    [Fact]
    public void VisibleWhenPatch_SurvivesSanitizeAndValidate_KeepsBpValidation()
    {
        DraftTriple baseTriple = new(
            ParseObject(Clinical),
            ParseObject(Ui),
            ParseObject(RulesWithBpValidation));
        DraftTriple patched = FormAiDraftPatch.Apply(
            baseTriple,
            JsonNode.Parse(VisibleWhenPatch)!);

        SanitizedAiTriple sanitized = FormAiSanitizer.Sanitize(
            patched.Clinical,
            patched.Ui,
            patched.Rules);

        Assert.True(
            sanitized.Rules["fields"]?["oxygen-saturation"]?["visibleWhen"]
                is JsonObject);
        Assert.Equal(
            1,
            (sanitized.Rules["validations"] as JsonArray)?.Count ?? 0);

        JsonSchemaValidator validator = CreateValidator();
        validator.ValidateFormDraft(
            sanitized.Clinical.ToJsonString(),
            sanitized.Ui.ToJsonString(),
            sanitized.Rules.ToJsonString());
    }

    [Fact]
    public void TruncatedMiniMaxContent_BarePatch_StillAppliesVisibleWhen()
    {
        const string truncated = """
            ":"La saturación de oxígeno ahora solo aparece.","assistantMessage":"Listo.","mode":"patch","patch":{"upsertRulesFields":{"oxygen-saturation":{"visibleWhen":{"op":"gte","args":[{"ref":"vital.heart.rate"},{"lit":100}]}}}}
            """;

        JsonObject? parsed = InvokeExtractJsonObject(truncated);
        Assert.NotNull(parsed);
        Assert.Equal("patch", parsed!["mode"]?.GetValue<string>());
        Assert.NotNull(parsed["patch"]?["upsertRulesFields"]);

        DraftTriple baseTriple = new(
            ParseObject(Clinical),
            ParseObject(Ui),
            ParseObject(RulesWithBpValidation));
        DraftTriple patched = FormAiDraftPatch.Apply(
            baseTriple,
            parsed["patch"]!);

        SanitizedAiTriple sanitized = FormAiSanitizer.Sanitize(
            patched.Clinical,
            patched.Ui,
            patched.Rules);

        Assert.True(
            sanitized.Rules["fields"]?["oxygen-saturation"]?["visibleWhen"]
                is JsonObject);
        Assert.Equal(
            1,
            (sanitized.Rules["validations"] as JsonArray)?.Count ?? 0);
    }

    [Theory]
    [InlineData(/*lang=json,strict*/ """{"op":"gte","args":[{"ref":"vital.heart.rate"},{"lit":100}]}""", true)]
    [InlineData(/*lang=json,strict*/ """{"op":"gte","args":[{"ref":"vital.heart.rate"},{"lit":100.5}]}""", true)]
    [InlineData(/*lang=json,strict*/ """{"op":"eq","args":[{"ref":"vital.spo2"},{"lit":"yes"}]}""", true)]
    [InlineData(/*lang=json,strict*/ """{"op":"gt","args":[{"ref":"vital.bp.systolic"},{"ref":"vital.bp.diastolic"}]}""", true)]
    public void BooleanExpressionOnly_NumericAndStringLiterals_AreAccepted(
        string expressionJson,
        bool expectValid)
    {
        string rules = /*lang=json,strict*/ $$"""
            {
              "schemaVersion": "1.0.0",
              "clinicalSchemaVersion": "1.0.0",
              "fields": {
                "oxygen-saturation": {
                  "visibleWhen": {{expressionJson}}
                }
              },
              "validations": []
            }
            """;

        if (expressionJson.Contains("\"gt\"", StringComparison.Ordinal))
        {
            rules = /*lang=json,strict*/ $$"""
                {
                  "schemaVersion": "1.0.0",
                  "clinicalSchemaVersion": "1.0.0",
                  "fields": {},
                  "validations": [
                    {
                      "code": "BP_SYSTOLIC_GT_DIASTOLIC",
                      "message": "sys > dia",
                      "assert": {{expressionJson}}
                    }
                  ]
                }
                """;
        }

        JsonSchemaValidator validator = CreateValidator();
        Exception? error = Record.Exception(
            () => validator.ValidateFormDraft(Clinical, Ui, rules));
        if (expectValid)
        {
            Assert.Null(error);
        }
        else
        {
            Assert.NotNull(error);
            Assert.Contains(
                "Invalid rules schema",
                error!.Message,
                StringComparison.Ordinal);
        }
    }

    private static JsonObject ParseObject(string json)
    {
        return JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException("Expected object.");
    }

    private static JsonSchemaValidator CreateValidator()
    {
        string schemaRoot = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "src",
                "Cynara.Infrastructure",
                "Schemas"));
        if (!Directory.Exists(Path.Combine(schemaRoot, "v1")))
        {
            schemaRoot = Path.Combine(AppContext.BaseDirectory, "Schemas");
        }

        return new JsonSchemaValidator(
            new SchemaFilePaths
            {
                ClinicalSchemaPath = Path.Combine(
                    schemaRoot,
                    "v1",
                    "clinical-schema.schema.json"),
                UiSchemaPath = Path.Combine(
                    schemaRoot,
                    "v1",
                    "ui-schema.schema.json"),
                RulesSchemaPath = Path.Combine(
                    schemaRoot,
                    "v1",
                    "rules-schema.schema.json"),
            });
    }

    private static JsonObject? InvokeExtractJsonObject(string raw)
    {
        System.Reflection.MethodInfo? method = typeof(FormAiService)
            .GetMethod(
                "ExtractJsonObject",
                System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        return method.Invoke(null, [raw]) as JsonObject;
    }
}
