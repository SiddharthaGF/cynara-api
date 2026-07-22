using System.Text.Json;
using System.Text.Json.Nodes;

using Cynara.Application.Common;
using Cynara.Application.Modules.Components.Persistence;
using Cynara.Domain.Components;

namespace Cynara.Application.Forms;

public sealed class FormCompiler(IComponentRepository components) : IFormCompiler
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
            RequireArray(clinicalRoot["fields"], "/fields"),
            "/fields",
            context).ConfigureAwait(false);

        var compiledClinical = new JsonObject
        {
            ["schemaVersion"] = clinicalRoot["schemaVersion"]?.DeepClone(),
        };

        if (clinicalRoot["$schema"] is JsonNode schemaUri)
        {
            compiledClinical["$schema"] = schemaUri.DeepClone();
        }

        compiledClinical["fields"] = compiledFields;

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
        if (rulesRoot["fields"] is JsonObject sourceFields)
        {
            foreach ((string fieldId, JsonNode? rules) in sourceFields.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                compiledFields[fieldId] = rules!.DeepClone();
            }
        }

        JsonArray? compiledValidations = null;
        if (rulesRoot["validations"] is JsonArray validations)
        {
            compiledValidations = [];
            foreach (JsonNode? validation in validations)
            {
                compiledValidations.Add(validation!.DeepClone());
            }
        }

        var compiledRules = new JsonObject
        {
            ["schemaVersion"] = rulesRoot["schemaVersion"]?.DeepClone(),
            ["clinicalSchemaVersion"] = rulesRoot["clinicalSchemaVersion"]?.DeepClone(),
            ["fields"] = compiledFields,
        };

        if (rulesRoot["$schema"] is JsonNode schemaUri)
        {
            compiledRules["$schema"] = schemaUri.DeepClone();
        }

        if (compiledValidations is not null)
        {
            compiledRules["validations"] = compiledValidations;
        }

        return compiledRules;
    }

    private static JsonObject? CompileUiSchema(JsonObject uiRoot, CompilationContext context)
    {
        var compiledFields = new JsonObject();
        if (uiRoot["fields"] is JsonObject sourceFields)
        {
            foreach ((string fieldId, JsonNode? presentation) in sourceFields.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                compiledFields[fieldId] = presentation!.DeepClone();
            }
        }

        foreach (ResolvedComponentDependency dependency in context.Dependencies.Values.OrderBy(
            static item => item.Code,
            StringComparer.Ordinal))
        {
            if (dependency.UiFields is null)
            {
                continue;
            }

            foreach ((string fieldId, JsonNode? presentation) in dependency.UiFields.OrderBy(
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
            ["schemaVersion"] = uiRoot["schemaVersion"]?.DeepClone(),
            ["clinicalSchemaVersion"] = uiRoot["clinicalSchemaVersion"]?.DeepClone(),
            ["fields"] = compiledFields,
        };

        if (uiRoot["$schema"] is JsonNode schemaUri)
        {
            compiledUi["$schema"] = schemaUri.DeepClone();
        }

        if (uiRoot["layout"] is JsonArray layout)
        {
            compiledUi["layout"] = CompileLayoutArray(layout, context);
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

        if (type == "field"
            && node["fieldId"] is JsonValue fieldIdValue
            && context.ExpandedReferenceLayouts.TryGetValue(fieldIdValue.GetValue<string>(), out JsonArray? expandedChildren))
        {
            var compiled = new JsonObject
            {
                ["type"] = "group",
                ["fieldId"] = fieldIdValue.DeepClone(),
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
        CopyIfPresent(node, result, "fieldId");
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

    private async Task<JsonArray> CompileFieldArrayAsync(JsonArray fields, string path, CompilationContext context)
    {
        var compiledFields = new JsonArray();
        for (int index = 0; index < fields.Count; index++)
        {
            JsonObject field = fields[index]!.AsObject();
            compiledFields.Add(await CompileFieldAsync(field, $"{path}/{index}", context).ConfigureAwait(false));
        }

        return compiledFields;
    }

    private async Task<JsonObject> CompileFieldAsync(JsonObject field, string path, CompilationContext context)
    {
        string type = RequireString(field["type"], $"{path}/type");
        if (type == "component-ref")
        {
            return await ExpandComponentReferenceAsync(field, path, context).ConfigureAwait(false);
        }

        JsonObject compiled = CloneFieldShell(field);
        if (field["items"] is JsonArray items)
        {
            compiled["items"] = await CompileFieldArrayAsync(items, $"{path}/items", context).ConfigureAwait(false);
        }

        return compiled;
    }

    private async Task<JsonObject> ExpandComponentReferenceAsync(
        JsonObject field,
        string path,
        CompilationContext context)
    {
        string componentCode = RequireString(field["componentCode"], $"{path}/componentCode");
        string? componentVersion = field["componentVersion"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(componentVersion))
        {
            throw new ValidationException(
                $"COMPONENT_VERSION_REQUIRED: component-ref at {path} must include componentVersion before publication.");
        }

        SemverRules.EnsureValid(componentVersion);

        if (context.ResolutionStack.Contains(componentCode))
        {
            throw new ValidationException(
                $"CIRCULAR_COMPONENT_REFERENCE: component '{componentCode}' references itself through {string.Join(" -> ", context.ResolutionStack)} -> {componentCode}.");
        }

        ResolvedComponentDependency dependency = await context.ResolveAsync(componentCode, componentVersion, path).ConfigureAwait(false);
        JsonObject componentClinical = ParseObject(dependency.ClinicalSchemaJson, $"component '{componentCode}' clinical schema");
        JsonArray componentFields = RequireArray(componentClinical["fields"], $"/components/{componentCode}/fields");

        context.ResolutionStack.Push(componentCode);
        JsonArray compiledItems;
        try
        {
            compiledItems = await CompileFieldArrayAsync(componentFields, $"/components/{componentCode}/fields", context).ConfigureAwait(false);
        }
        finally
        {
            _ = context.ResolutionStack.Pop();
        }

        string fieldId = RequireString(field["id"], $"{path}/id");
        context.ExpandedReferenceLayouts[fieldId] = dependency.LayoutChildren is JsonArray layoutChildren
            ? CompileLayoutArray(layoutChildren, context)
            : BuildDefaultLayout(compiledItems);

        var compiled = new JsonObject
        {
            ["id"] = field["id"]?.DeepClone(),
            ["code"] = field["code"]?.DeepClone(),
            ["type"] = "group",
            ["items"] = compiledItems,
        };

        CopyIfPresent(field, compiled, "required");
        CopyIfPresent(field, compiled, "readOnly");
        CopyIfPresent(field, compiled, "description");

        return compiled;
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
                ["fieldId"] = itemId,
            });
        }

        return layout;
    }

    private static JsonObject CloneFieldShell(JsonObject field)
    {
        var clone = new JsonObject();
        foreach ((string propertyName, JsonNode? value) in field)
        {
            if (propertyName == "items")
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

    private sealed class CompilationContext(
        IComponentRepository components,
        CancellationToken cancellationToken)
    {
        public Stack<string> ResolutionStack { get; } = new();

        public Dictionary<string, ResolvedComponentDependency> Dependencies { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, JsonArray> ExpandedReferenceLayouts { get; } = new(StringComparer.Ordinal);

        public async Task<ResolvedComponentDependency> ResolveAsync(
            string componentCode,
            string componentVersion,
            string refPath)
        {
            string dependencyKey = $"{componentCode}@{componentVersion}";
            if (Dependencies.TryGetValue(dependencyKey, out ResolvedComponentDependency? cached))
            {
                return cached;
            }

            ComponentVersion version = await components.FindPublishedVersionAsync(
                componentCode,
                componentVersion,
                cancellationToken).ConfigureAwait(false)
                ?? throw new ValidationException(
                    $"COMPONENT_VERSION_NOT_FOUND: component '{componentCode}' version '{componentVersion}' referenced at {refPath} was not found or is not published.");

            JsonObject? uiFields = null;
            JsonArray? layoutChildren = null;
            if (version.UiSchemaJson is not null)
            {
                JsonObject uiRoot = JsonNode.Parse(version.UiSchemaJson)?.AsObject()
                    ?? throw new ValidationException($"Invalid UI schema for component '{componentCode}' version '{componentVersion}'.");
                uiFields = uiRoot["fields"] as JsonObject;
                layoutChildren = uiRoot["layout"] as JsonArray;
            }

            var dependency = new ResolvedComponentDependency(
                componentCode,
                componentVersion,
                version.ContentHash ?? string.Empty,
                version.ClinicalSchemaJson,
                uiFields,
                layoutChildren);

            Dependencies[dependencyKey] = dependency;
            return dependency;
        }

        public string BuildDependencyMetadataJson(string compiledClinicalJson, string? compiledRulesJson)
        {
            var metadata = new JsonObject
            {
                ["components"] = new JsonArray(
                    [.. Dependencies.Values
                        .OrderBy(static item => item.Code, StringComparer.Ordinal)
                        .ThenBy(static item => item.Version, SemverRules.StringComparer)
                        .Select(static item => new JsonObject
                        {
                            ["code"] = item.Code,
                            ["version"] = item.Version,
                            ["contentHash"] = item.ContentHash,
                        })]),
            };

            if (compiledRulesJson is not null)
            {
                RuleDependencyMetadata ruleMetadata = FormRuleAnalyzer.Analyze(compiledClinicalJson, compiledRulesJson);
                metadata["rules"] = new JsonObject
                {
                    ["calculatedFieldIds"] = new JsonArray(
                        [.. ruleMetadata.CalculatedFieldIds.Select(static id => JsonValue.Create(id))]),
                    ["evaluationOrder"] = new JsonArray(
                        [.. ruleMetadata.EvaluationOrder.Select(static id => JsonValue.Create(id))]),
                };
            }

            return CanonicalJsonSerializer.Serialize(metadata);
        }
    }

    private sealed record ResolvedComponentDependency(
        string Code,
        string Version,
        string ContentHash,
        string ClinicalSchemaJson,
        JsonObject? UiFields,
        JsonArray? LayoutChildren);
}
