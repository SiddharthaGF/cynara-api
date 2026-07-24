using System.Text.Json;
using System.Text.Json.Nodes;

using Cynara.Application.Common;
using Cynara.Application.Modules.Components.Persistence;

namespace Cynara.Application.Forms;

public sealed partial class FormCompiler(IComponentRepository components) : IFormCompiler
{
    public async Task<FormCompilationResult> CompileAsync(
        string clinicalSchemaJson,
        string? uiSchemaJson,
        string? rulesSchemaJson,
        CancellationToken cancellationToken)
    {
        JsonObject clinicalRoot = ParseObject(clinicalSchemaJson, "clinical schema");
        JsonObject? uiRoot = uiSchemaJson is null ? null : ParseObject(uiSchemaJson, "UI schema");
        JsonObject? rulesRoot = rulesSchemaJson is null ? null : ParseObject(rulesSchemaJson, "rules schema");

        var context = new CompilationContext(components, cancellationToken);
        JsonArray compiledFields = await CompileFieldArrayAsync(
            RequireArray(clinicalRoot[SchemaJsonKeys.Fields], "/fields"),
            "/fields",
            context).ConfigureAwait(false);

        var compiledClinical = new JsonObject
        {
            [SchemaJsonKeys.SchemaVersion] = clinicalRoot[SchemaJsonKeys.SchemaVersion]?.DeepClone(),
        };

        if (clinicalRoot[SchemaJsonKeys.Schema] is JsonNode schemaUri)
        {
            compiledClinical[SchemaJsonKeys.Schema] = schemaUri.DeepClone();
        }

        compiledClinical[SchemaJsonKeys.Fields] = compiledFields;

        JsonObject? compiledUi = uiRoot is null
            ? null
            : CompileUiSchema(uiRoot, context);

        JsonObject? compiledRules = rulesRoot is null
            ? null
            : CompileRulesSchema(rulesRoot);

        string compiledClinicalJson = CanonicalJsonSerializer.Serialize(compiledClinical);
        string? compiledUiJson = compiledUi is null ? null : CanonicalJsonSerializer.Serialize(compiledUi);
        string? compiledRulesJson = compiledRules is null ? null : CanonicalJsonSerializer.Serialize(compiledRules);
        string dependencyMetadataJson = context.BuildDependencyMetadataJson(compiledClinicalJson, compiledRulesJson);
        string contentHash = ContentHashCalculator.Compute(compiledClinicalJson, compiledUiJson, compiledRulesJson);

        return new FormCompilationResult(
            compiledClinicalJson,
            compiledUiJson,
            compiledRulesJson,
            dependencyMetadataJson,
            contentHash);
    }

    private static JsonObject CompileRulesSchema(JsonObject rulesRoot)
    {
        var compiledFields = new JsonObject();
        if (rulesRoot[SchemaJsonKeys.Fields] is JsonObject sourceFields)
        {
            foreach ((string fieldId, JsonNode? rules) in sourceFields.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                compiledFields[fieldId] = rules!.DeepClone();
            }
        }

        JsonArray? compiledValidations = null;
        if (rulesRoot[SchemaJsonKeys.Validations] is JsonArray validations)
        {
            compiledValidations = [];
            foreach (JsonNode? validation in validations)
            {
                compiledValidations.Add(validation!.DeepClone());
            }
        }

        var compiledRules = new JsonObject
        {
            [SchemaJsonKeys.SchemaVersion] =
                rulesRoot[SchemaJsonKeys.SchemaVersion]?.DeepClone(),
            [SchemaJsonKeys.ClinicalSchemaVersion] =
                rulesRoot[SchemaJsonKeys.ClinicalSchemaVersion]?.DeepClone(),
            [SchemaJsonKeys.Fields] = compiledFields,
        };

        if (rulesRoot[SchemaJsonKeys.Schema] is JsonNode schemaUri)
        {
            compiledRules[SchemaJsonKeys.Schema] = schemaUri.DeepClone();
        }

        if (compiledValidations is not null)
        {
            compiledRules[SchemaJsonKeys.Validations] = compiledValidations;
        }

        return compiledRules;
    }

    private static JsonObject? CompileUiSchema(JsonObject uiRoot, CompilationContext context)
    {
        var compiledFields = new JsonObject();
        if (uiRoot[SchemaJsonKeys.Fields] is JsonObject sourceFields)
        {
            foreach ((string fieldId, JsonNode? presentation) in sourceFields.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                compiledFields[fieldId] = presentation!.DeepClone();
            }
        }

        foreach (JsonObject uiFields in context.Dependencies.Values
            .OrderBy(static item => item.Code, StringComparer.Ordinal)
            .Select(static dependency => dependency.UiFields)
            .Where(static fields => fields is not null)
            .Select(static fields => fields!))
        {
            foreach ((string fieldId, JsonNode? presentation) in uiFields.OrderBy(
                static pair => pair.Key,
                StringComparer.Ordinal))
            {
                if (presentation is not null)
                {
                    compiledFields[fieldId] = presentation.DeepClone();
                }
            }
        }

        var compiledUi = new JsonObject
        {
            [SchemaJsonKeys.SchemaVersion] =
                uiRoot[SchemaJsonKeys.SchemaVersion]?.DeepClone(),
            [SchemaJsonKeys.ClinicalSchemaVersion] =
                uiRoot[SchemaJsonKeys.ClinicalSchemaVersion]?.DeepClone(),
            [SchemaJsonKeys.Fields] = compiledFields,
        };

        if (uiRoot[SchemaJsonKeys.Schema] is JsonNode schemaUri)
        {
            compiledUi[SchemaJsonKeys.Schema] = schemaUri.DeepClone();
        }

        if (uiRoot[SchemaJsonKeys.Layout] is JsonArray layout)
        {
            compiledUi[SchemaJsonKeys.Layout] = CompileLayoutArray(layout, context);
        }

        return compiledUi;
    }

    private static JsonArray CompileLayoutArray(JsonArray layout, CompilationContext context)
    {
        var compiledLayout = new JsonArray();
        foreach (JsonNode? node in layout)
        {
            compiledLayout.Add(CompileLayoutNode(node!.AsObject(), context));
        }

        return compiledLayout;
    }

    private static JsonObject CompileLayoutNode(JsonObject node, CompilationContext context)
    {
        string type = RequireString(node["type"], "layout type");

        if (string.Equals(type, "field", StringComparison.Ordinal)
            && node[SchemaJsonKeys.FieldId] is JsonValue fieldIdValue
            && context.ExpandedReferenceLayouts.TryGetValue(
                fieldIdValue.GetValue<string>(),
                out JsonArray? expandedChildren))
        {
            var compiled = new JsonObject
            {
                ["type"] = FieldTypeNames.Group,
                [SchemaJsonKeys.FieldId] = fieldIdValue.DeepClone(),
                ["children"] = expandedChildren.DeepClone(),
            };

            CopyIfPresent(node, compiled, "id");
            CopyIfPresent(node, compiled, "title");
            CopyIfPresent(node, compiled, "description");
            return compiled;
        }

        var result = new JsonObject
        {
            ["type"] = type,
        };

        CopyIfPresent(node, result, "id");
        CopyIfPresent(node, result, "title");
        CopyIfPresent(node, result, "description");
        CopyIfPresent(node, result, SchemaJsonKeys.FieldId);
        CopyIfPresent(node, result, "addButtonLabel");
        CopyIfPresent(node, result, "removeButtonLabel");

        if (node["children"] is JsonArray children)
        {
            result["children"] = CompileLayoutArray(children, context);
        }

        if (node["itemTemplate"] is JsonArray itemTemplate)
        {
            result["itemTemplate"] = CompileLayoutArray(itemTemplate, context);
        }

        return result;
    }

    private static JsonArray BuildDefaultLayout(JsonArray compiledItems)
    {
        var layout = new JsonArray();
        foreach (JsonNode? item in compiledItems)
        {
            string itemId = RequireString(item!["id"], "compiled field id");
            layout.Add(new JsonObject
            {
                ["type"] = "field",
                [SchemaJsonKeys.FieldId] = itemId,
            });
        }

        return layout;
    }

    private static JsonObject CloneFieldShell(JsonObject field)
    {
        var clone = new JsonObject();
        foreach ((string propertyName, JsonNode? value) in field)
        {
            if (string.Equals(propertyName, SchemaJsonKeys.Items, StringComparison.Ordinal))
            {
                continue;
            }

            clone[propertyName] = value?.DeepClone();
        }

        return clone;
    }

    private static void CopyIfPresent(JsonObject source, JsonObject target, string propertyName)
    {
        if (source[propertyName] is JsonNode value)
        {
            target[propertyName] = value.DeepClone();
        }
    }

    private static JsonObject ParseObject(string json, string label)
    {
        try
        {
            return JsonNode.Parse(json)?.AsObject()
                ?? throw new ValidationException($"Invalid {label}: expected a JSON object.");
        }
        catch (JsonException exception)
        {
            throw new ValidationException($"Invalid {label}: {exception.Message}");
        }
    }

    private static JsonArray RequireArray(JsonNode? node, string path)
    {
        return node as JsonArray
            ?? throw new ValidationException($"Expected array at {path}.");
    }

    private static string RequireString(JsonNode? node, string path)
    {
        return node?.GetValue<string>() is { Length: > 0 } value
            ? value
            : throw new ValidationException($"Expected non-empty string at {path}.");
    }
}
