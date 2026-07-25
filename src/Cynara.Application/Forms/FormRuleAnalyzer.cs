using System.Globalization;
using System.Text.Json.Nodes;

using Cynara.Application.Common;

namespace Cynara.Application.Forms;

public static class FormRuleAnalyzer
{
    public static RuleDependencyMetadata Analyze(string clinicalSchemaJson, string rulesSchemaJson)
    {
        JsonObject clinicalRoot = JsonParsing.ParseObject(clinicalSchemaJson, "clinical schema");
        JsonObject rulesRoot = JsonParsing.ParseObject(rulesSchemaJson, "rules schema");
        Dictionary<string, ClinicalFieldIndex.FieldInfo> fieldsById =
            ClinicalFieldIndex.BuildById(clinicalRoot);

        if (rulesRoot[SchemaJsonKeys.Fields] is not JsonObject fieldRules)
        {
            return new RuleDependencyMetadata([], []);
        }

        var calculatedFieldIds = new List<string>();
        var dependencies = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach ((string fieldId, JsonNode? rulesNode) in fieldRules)
        {
            if (rulesNode is not JsonObject rules)
            {
                continue;
            }

            if (rules[SchemaJsonKeys.Calculate] is not JsonNode calculateNode)
            {
                continue;
            }

            if (!fieldsById.ContainsKey(fieldId))
            {
                continue;
            }

            calculatedFieldIds.Add(fieldId);
            dependencies[fieldId] = CollectReferences(calculateNode)
                .Select(code => ResolveFieldId(fieldsById, code))
                .Where(item => item is not null)
                .Select(item => item!)
                .ToHashSet(StringComparer.Ordinal);
        }

        List<string> evaluationOrder = TopologicalSort(calculatedFieldIds, dependencies);
        return new RuleDependencyMetadata(calculatedFieldIds, evaluationOrder);
    }

    public static void ValidateDependencies(string clinicalSchemaJson, string rulesSchemaJson)
    {
        JsonObject clinicalRoot = JsonParsing.ParseObject(clinicalSchemaJson, "clinical schema");
        JsonObject rulesRoot = JsonParsing.ParseObject(rulesSchemaJson, "rules schema");
        Dictionary<string, ClinicalFieldIndex.FieldInfo> fieldsById =
            ClinicalFieldIndex.BuildById(clinicalRoot);
        Dictionary<string, ClinicalFieldIndex.FieldInfo> fieldsByCode =
            ClinicalFieldIndex.BuildByCode(clinicalRoot);

        ValidateClinicalVersionMatch(clinicalRoot, rulesRoot);

        if (rulesRoot[SchemaJsonKeys.Fields] is JsonObject fieldRules)
        {
            ValidateFieldRules(fieldRules, fieldsById, fieldsByCode);
        }

        if (rulesRoot[SchemaJsonKeys.Validations] is JsonArray validations)
        {
            ValidateValidationEntries(validations, fieldsByCode);
        }

        RuleDependencyMetadata metadata = Analyze(clinicalSchemaJson, rulesSchemaJson);
        if (metadata.EvaluationOrder.Count != metadata.CalculatedFieldIds.Count)
        {
            throw new ValidationException(
                "RULE_CYCLIC_DEPENDENCY: calculated fields contain a cyclic dependency.");
        }
    }

    internal static HashSet<string> CollectReferences(JsonNode expression)
    {
        var references = new HashSet<string>(StringComparer.Ordinal);
        CollectReferencesRecursive(expression, references);
        return references;
    }

    private static void ValidateClinicalVersionMatch(
        JsonObject clinicalRoot,
        JsonObject rulesRoot)
    {
        if (rulesRoot[SchemaJsonKeys.ClinicalSchemaVersion]?.GetValue<string>()
                is string rulesClinicalVersion
            && clinicalRoot[SchemaJsonKeys.SchemaVersion]?.GetValue<string>()
                is string clinicalVersion
            && !string.Equals(
                rulesClinicalVersion,
                clinicalVersion,
                StringComparison.Ordinal))
        {
            throw new ValidationException(
                $"RULE_CLINICAL_VERSION_MISMATCH: rules clinicalSchemaVersion '{rulesClinicalVersion}' does not match clinical schemaVersion '{clinicalVersion}'.");
        }
    }

    private static void ValidateFieldRules(
        JsonObject fieldRules,
        Dictionary<string, ClinicalFieldIndex.FieldInfo> fieldsById,
        Dictionary<string, ClinicalFieldIndex.FieldInfo> fieldsByCode)
    {
        foreach ((string fieldId, JsonNode? rulesNode) in fieldRules)
        {
            string path = $"/fields/{fieldId}";
            if (!fieldsById.TryGetValue(fieldId, out ClinicalFieldIndex.FieldInfo? fieldInfo))
            {
                throw new ValidationException(
                    $"RULE_UNKNOWN_FIELD: rules reference unknown clinical field id '{fieldId}' at {path}.");
            }

            if (rulesNode is not JsonObject rules)
            {
                continue;
            }

            ValidateExpressionReferences(
                rules["visibleWhen"],
                $"{path}/visibleWhen",
                fieldsByCode);
            ValidateExpressionReferences(
                rules["enabledWhen"],
                $"{path}/enabledWhen",
                fieldsByCode);
            ValidateExpressionReferences(
                rules["requiredWhen"],
                $"{path}/requiredWhen",
                fieldsByCode);
            ValidateExpressionReferences(
                rules[SchemaJsonKeys.Calculate],
                $"{path}/calculate",
                fieldsByCode);

            if (rules[SchemaJsonKeys.Calculate] is not null && !fieldInfo.ReadOnly)
            {
                throw new ValidationException(
                    $"RULE_CALCULATE_NOT_READONLY: calculated field '{fieldId}' at {path}/calculate must be readOnly in the clinical schema.");
            }

            if (rules[SchemaJsonKeys.Calculate] is JsonNode calculateNode)
            {
                string targetCode = fieldInfo.Code;
                if (CollectReferences(calculateNode).Any(
                        code => string.Equals(
                            code,
                            targetCode,
                            StringComparison.Ordinal)))
                {
                    throw new ValidationException(
                        $"RULE_SELF_REFERENCE: calculated field '{fieldId}' at {path}/calculate must not reference its own code '{targetCode}'.");
                }
            }
        }
    }

    private static void ValidateValidationEntries(
        JsonArray validations,
        Dictionary<string, ClinicalFieldIndex.FieldInfo> fieldsByCode)
    {
        var seenCodes = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < validations.Count; index++)
        {
            JsonObject validation = validations[index]!.AsObject();
            string path = string.Create(
                CultureInfo.InvariantCulture,
                $"/validations/{index}");
            string code = validation[SchemaJsonKeys.Code]?.GetValue<string>()
                ?? throw new ValidationException(
                    $"Expected validation code at {path}/code.");

            if (!seenCodes.Add(code))
            {
                throw new ValidationException(
                    $"RULE_DUPLICATE_VALIDATION_CODE: validation code '{code}' at {path}/code is duplicated.");
            }

            ValidateExpressionReferences(
                validation["when"],
                $"{path}/when",
                fieldsByCode);
            ValidateExpressionReferences(
                validation["assert"],
                $"{path}/assert",
                fieldsByCode);
        }
    }

    private static void ValidateExpressionReferences(
        JsonNode? expression,
        string path,
        Dictionary<string, ClinicalFieldIndex.FieldInfo> fieldsByCode)
    {
        if (expression is null)
        {
            return;
        }

        string? unknownCode = CollectReferences(expression)
            .FirstOrDefault(code => !fieldsByCode.ContainsKey(code));
        if (unknownCode is not null)
        {
            throw new ValidationException(
                $"RULE_UNKNOWN_FIELD_REF: expression at {path} references unknown field code '{unknownCode}'.");
        }
    }

    private static void CollectReferencesRecursive(JsonNode node, HashSet<string> references)
    {
        if (node is not JsonObject obj)
        {
            return;
        }

        if (obj["ref"]?.GetValue<string>() is { Length: > 0 } fieldCode)
        {
            _ = references.Add(fieldCode);
            return;
        }

        if (obj["args"] is not JsonArray args)
        {
            return;
        }

        foreach (JsonNode arg in args.Where(static arg => arg is not null).Cast<JsonNode>())
        {
            CollectReferencesRecursive(arg, references);
        }
    }

    private static string? ResolveFieldId(
        Dictionary<string, ClinicalFieldIndex.FieldInfo> fieldsById,
        string code)
    {
        return fieldsById.Values
            .Where(field => string.Equals(field.Code, code, StringComparison.Ordinal))
            .Select(field => field.Id)
            .FirstOrDefault();
    }

    private static List<string> TopologicalSort(
        IReadOnlyList<string> calculatedFieldIds,
        Dictionary<string, HashSet<string>> dependencies)
    {
        var inDegree = calculatedFieldIds.ToDictionary(
            static id => id,
            static _ => 0,
            StringComparer.Ordinal);
        foreach (string fieldId in calculatedFieldIds)
        {
            foreach (string dependencyId in (dependencies.GetValueOrDefault(fieldId) ?? [])
                .Where(inDegree.ContainsKey))
            {
                inDegree[fieldId]++;
            }
        }

        var queue = new Queue<string>(
            inDegree.Where(static pair => pair.Value == 0).Select(static pair => pair.Key));
        var order = new List<string>();

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            order.Add(current);

            foreach ((string fieldId, HashSet<string> fieldDependencies) in dependencies)
            {
                if (!fieldDependencies.Contains(current))
                {
                    continue;
                }

                inDegree[fieldId]--;
                if (inDegree[fieldId] == 0)
                {
                    queue.Enqueue(fieldId);
                }
            }
        }

        return order;
    }
}
