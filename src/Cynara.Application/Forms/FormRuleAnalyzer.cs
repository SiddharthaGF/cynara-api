using System.Text.Json.Nodes;

namespace Cynara.Application.Forms;

public static class FormRuleAnalyzer
{
    public static RuleDependencyMetadata Analyze(string clinicalSchemaJson, string rulesSchemaJson)
    {
        JsonObject clinicalRoot = ParseObject(clinicalSchemaJson, "clinical schema");
        JsonObject rulesRoot = ParseObject(rulesSchemaJson, "rules schema");
        Dictionary<string, ClinicalFieldIndex.FieldInfo> fieldsById = ClinicalFieldIndex.BuildById(clinicalRoot);

        if (rulesRoot["fields"] is not JsonObject fieldRules)
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

            if (rules["calculate"] is not JsonNode calculateNode)
            {
                continue;
            }

            if (!fieldsById.TryGetValue(fieldId, out ClinicalFieldIndex.FieldInfo? fieldInfo))
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
        JsonObject clinicalRoot = ParseObject(clinicalSchemaJson, "clinical schema");
        JsonObject rulesRoot = ParseObject(rulesSchemaJson, "rules schema");
        Dictionary<string, ClinicalFieldIndex.FieldInfo> fieldsById = ClinicalFieldIndex.BuildById(clinicalRoot);
        Dictionary<string, ClinicalFieldIndex.FieldInfo> fieldsByCode = ClinicalFieldIndex.BuildByCode(clinicalRoot);

        if (rulesRoot["clinicalSchemaVersion"]?.GetValue<string>() is string rulesClinicalVersion
            && clinicalRoot["schemaVersion"]?.GetValue<string>() is string clinicalVersion
            && rulesClinicalVersion != clinicalVersion)
        {
            throw new ValidationException(
                $"RULE_CLINICAL_VERSION_MISMATCH: rules clinicalSchemaVersion '{rulesClinicalVersion}' does not match clinical schemaVersion '{clinicalVersion}'.");
        }

        if (rulesRoot["fields"] is JsonObject fieldRules)
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

                ValidateExpressionReferences(rules["visibleWhen"], $"{path}/visibleWhen", fieldsByCode);
                ValidateExpressionReferences(rules["enabledWhen"], $"{path}/enabledWhen", fieldsByCode);
                ValidateExpressionReferences(rules["requiredWhen"], $"{path}/requiredWhen", fieldsByCode);
                ValidateExpressionReferences(rules["calculate"], $"{path}/calculate", fieldsByCode);

                if (rules["calculate"] is not null && !fieldInfo.ReadOnly)
                {
                    throw new ValidationException(
                        $"RULE_CALCULATE_NOT_READONLY: calculated field '{fieldId}' at {path}/calculate must be readOnly in the clinical schema.");
                }

                if (rules["calculate"] is JsonNode calculateNode)
                {
                    string targetCode = fieldInfo.Code;
                    foreach (string referencedCode in CollectReferences(calculateNode))
                    {
                        if (referencedCode == targetCode)
                        {
                            throw new ValidationException(
                                $"RULE_SELF_REFERENCE: calculated field '{fieldId}' at {path}/calculate must not reference its own code '{targetCode}'.");
                        }
                    }
                }
            }
        }

        if (rulesRoot["validations"] is JsonArray validations)
        {
            var seenCodes = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < validations.Count; index++)
            {
                JsonObject validation = validations[index]!.AsObject();
                string path = $"/validations/{index}";
                string code = validation["code"]?.GetValue<string>()
                    ?? throw new ValidationException($"Expected validation code at {path}/code.");

                if (!seenCodes.Add(code))
                {
                    throw new ValidationException(
                        $"RULE_DUPLICATE_VALIDATION_CODE: validation code '{code}' at {path}/code is duplicated.");
                }

                ValidateExpressionReferences(validation["when"], $"{path}/when", fieldsByCode);
                ValidateExpressionReferences(validation["assert"], $"{path}/assert", fieldsByCode);
            }
        }

        RuleDependencyMetadata metadata = Analyze(clinicalSchemaJson, rulesSchemaJson);
        if (metadata.EvaluationOrder.Count != metadata.CalculatedFieldIds.Count)
        {
            throw new ValidationException(
                "RULE_CYCLIC_DEPENDENCY: calculated fields contain a cyclic dependency.");
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

        foreach (string referencedCode in CollectReferences(expression))
        {
            if (!fieldsByCode.ContainsKey(referencedCode))
            {
                throw new ValidationException(
                    $"RULE_UNKNOWN_FIELD_REF: expression at {path} references unknown field code '{referencedCode}'.");
            }
        }
    }

    internal static HashSet<string> CollectReferences(JsonNode expression)
    {
        var references = new HashSet<string>(StringComparer.Ordinal);
        CollectReferencesRecursive(expression, references);
        return references;
    }

    private static void CollectReferencesRecursive(JsonNode node, HashSet<string> references)
    {
        if (node is JsonObject obj)
        {
            if (obj["ref"]?.GetValue<string>() is { Length: > 0 } fieldCode)
            {
                _ = references.Add(fieldCode);
                return;
            }

            if (obj["args"] is JsonArray args)
            {
                foreach (JsonNode? arg in args)
                {
                    if (arg is not null)
                    {
                        CollectReferencesRecursive(arg, references);
                    }
                }
            }
        }
    }

    private static string? ResolveFieldId(
        Dictionary<string, ClinicalFieldIndex.FieldInfo> fieldsById,
        string code)
    {
        foreach (ClinicalFieldIndex.FieldInfo field in fieldsById.Values)
        {
            if (field.Code == code)
            {
                return field.Id;
            }
        }

        return null;
    }

    private static List<string> TopologicalSort(
        IReadOnlyList<string> calculatedFieldIds,
        Dictionary<string, HashSet<string>> dependencies)
    {
        var inDegree = calculatedFieldIds.ToDictionary(static id => id, static id => 0, StringComparer.Ordinal);
        foreach (string fieldId in calculatedFieldIds)
        {
            foreach (string dependencyId in dependencies.GetValueOrDefault(fieldId) ?? [])
            {
                if (inDegree.ContainsKey(dependencyId))
                {
                    inDegree[fieldId]++;
                }
            }
        }

        var queue = new Queue<string>(inDegree.Where(static pair => pair.Value == 0).Select(static pair => pair.Key));
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

    private static JsonObject ParseObject(string json, string label)
    {
        try
        {
            return JsonNode.Parse(json)?.AsObject()
                ?? throw new ValidationException($"Invalid {label}: expected a JSON object.");
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new ValidationException($"Invalid {label}: {exception.Message}");
        }
    }
}
