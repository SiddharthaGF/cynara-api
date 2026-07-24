using System.Text.Json;

using Cynara.Application;
using Cynara.Application.Forms;
using Cynara.Application.Schemas;

using Json.Schema;

namespace Cynara.Infrastructure.Schemas;

public sealed class JsonSchemaValidator(SchemaFilePaths options) : ISchemaValidator
{
    private readonly JsonSchema clinicalSchema = JsonSchema.FromFile(options.ClinicalSchemaPath);
    private readonly JsonSchema uiSchema = JsonSchema.FromFile(options.UiSchemaPath);
    private readonly JsonSchema rulesSchema = JsonSchema.FromFile(options.RulesSchemaPath);

    public void ValidateComponentDraft(string clinicalSchemaJson, string? uiSchemaJson)
    {
        ValidateDraft(clinicalSchemaJson, uiSchemaJson, rulesSchemaJson: null);
    }

    public void ValidateFormDraft(string clinicalSchemaJson, string? uiSchemaJson, string? rulesSchemaJson = null)
    {
        ValidateDraft(clinicalSchemaJson, uiSchemaJson, rulesSchemaJson);
    }

    private static void ValidateJson(JsonSchema schema, string json, string label)
    {
        using var document = JsonDocument.Parse(json);
        EvaluationResults results = schema.Evaluate(document.RootElement, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
        });

        if (results.IsValid)
        {
            return;
        }

        string details = FormatErrors(results);
        throw new ValidationException($"Invalid {label}: {details}");
    }

    private static string FormatErrors(EvaluationResults results)
    {
        List<string> messages = [];
        CollectErrors(results, messages);
        return messages.Count == 0 ? "schema validation failed" : string.Join("; ", messages.Take(5));
    }

    private static void CollectErrors(EvaluationResults results, List<string> messages)
    {
        if (results.Errors is not null)
        {
            foreach (KeyValuePair<string, string> error in results.Errors)
            {
                messages.Add($"{error.Key}: {error.Value}");
            }
        }

        if (results.Details is null)
        {
            return;
        }

        foreach (EvaluationResults detail in results.Details)
        {
            CollectErrors(detail, messages);
        }
    }

    private void ValidateDraft(string clinicalSchemaJson, string? uiSchemaJson, string? rulesSchemaJson)
    {
        ValidateJson(clinicalSchema, clinicalSchemaJson, "clinical schema");
        if (uiSchemaJson is not null)
        {
            ValidateJson(uiSchema, uiSchemaJson, "UI schema");
        }

        if (rulesSchemaJson is not null)
        {
            ValidateJson(rulesSchema, rulesSchemaJson, "rules schema");
            FormRuleAnalyzer.ValidateDependencies(clinicalSchemaJson, rulesSchemaJson);
        }
    }
}

public sealed class SchemaFilePaths
{
    public required string ClinicalSchemaPath { get; init; }

    public required string UiSchemaPath { get; init; }

    public required string RulesSchemaPath { get; init; }

    public static SchemaFilePaths FromBaseDirectory(string? baseDirectory = null)
    {
        string schemaRoot = Path.Combine(
            baseDirectory ?? AppContext.BaseDirectory,
            "Schemas");
        return new SchemaFilePaths
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
        };
    }
}
