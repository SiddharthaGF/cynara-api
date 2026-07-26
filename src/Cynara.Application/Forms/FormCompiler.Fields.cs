using System.Globalization;
using System.Text.Json.Nodes;

using Cynara.Application.Common;
using Cynara.Application.Modules.Components.Persistence;
using Cynara.Domain.Components;

namespace Cynara.Application.Forms;

public sealed partial class FormCompiler
{
    private async Task<JsonArray> CompileFieldArrayAsync(JsonArray fields, string path, CompilationContext context)
    {
        var compiledFields = new JsonArray();
        for (int index = 0; index < fields.Count; index++)
        {
            JsonObject field = fields[index]!.AsObject();
            compiledFields.Add(await CompileFieldAsync(field, string.Create(CultureInfo.InvariantCulture, $"{path}/{index}"), context).ConfigureAwait(false));
        }

        return compiledFields;
    }

    private async Task<JsonObject> CompileFieldAsync(JsonObject field, string path, CompilationContext context)
    {
        string type = RequireString(field["type"], $"{path}/type");
        if (string.Equals(type, "component-ref", StringComparison.Ordinal))
        {
            return await ExpandComponentReferenceAsync(field, path, context).ConfigureAwait(false);
        }

        JsonObject compiled = CloneFieldShell(field);
        if (field[SchemaJsonKeys.Items] is JsonArray items)
        {
            compiled[SchemaJsonKeys.Items] = await CompileFieldArrayAsync(items, $"{path}/items", context).ConfigureAwait(false);
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
        JsonObject componentClinical = JsonParsing.ParseObject(dependency.ClinicalSchemaJson, $"component '{componentCode}' clinical schema");
        JsonArray componentFields = RequireArray(componentClinical[SchemaJsonKeys.Fields], $"/components/{componentCode}/fields");

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
            ["type"] = FieldTypeNames.Group,
            [SchemaJsonKeys.Items] = compiledItems,
        };

        CopyIfPresent(field, compiled, "required");
        CopyIfPresent(field, compiled, "readOnly");
        CopyIfPresent(field, compiled, "description");

        return compiled;
    }

    private sealed class CompilationContext(
        IComponentRepository components,
        Guid hospitalId,
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
                hospitalId,
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
                uiFields = uiRoot[SchemaJsonKeys.Fields] as JsonObject;
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
