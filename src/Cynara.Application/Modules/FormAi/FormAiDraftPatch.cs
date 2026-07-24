using System.Text.Json.Nodes;

using Cynara.Application.Common;

namespace Cynara.Application.Modules.FormAi;

internal static class FormAiDraftPatch
{
    public static DraftTriple Empty(string schemaVersion = "1.0.0")
    {
        return new DraftTriple(
            new JsonObject
            {
                [SchemaJsonKeys.SchemaVersion] = schemaVersion,
                [SchemaJsonKeys.Fields] = new JsonArray(),
            },
            new JsonObject
            {
                [SchemaJsonKeys.SchemaVersion] = schemaVersion,
                [SchemaJsonKeys.ClinicalSchemaVersion] = schemaVersion,
                [SchemaJsonKeys.Fields] = new JsonObject(),
                [SchemaJsonKeys.Layout] = new JsonArray(),
            },
            new JsonObject
            {
                [SchemaJsonKeys.SchemaVersion] = schemaVersion,
                [SchemaJsonKeys.ClinicalSchemaVersion] = schemaVersion,
                [SchemaJsonKeys.Fields] = new JsonObject(),
                [SchemaJsonKeys.Validations] = new JsonArray(),
            });
    }

    public static DraftTriple Apply(DraftTriple baseTriple, JsonNode patchNode)
    {
        if (patchNode is not JsonObject patch)
        {
            throw new ValidationException(
                "AI patch response must include a patch object.");
        }

        if (patch["clear"]?.GetValue<bool>() is true)
        {
            return Empty(SchemaVersionOf(baseTriple.Clinical));
        }

        JsonObject clinical = CloneObject(baseTriple.Clinical);
        JsonObject ui = CloneObject(baseTriple.Ui);
        JsonObject rules = CloneObject(baseTriple.Rules);
        JsonArray clinicalFields = CloneArray(clinical[SchemaJsonKeys.Fields]);
        JsonObject uiFields = CloneObject(ui[SchemaJsonKeys.Fields]);
        JsonObject rulesFields = CloneObject(rules[SchemaJsonKeys.Fields]);
        JsonArray layout = CloneArray(ui[SchemaJsonKeys.Layout]);
        JsonArray validations = CloneArray(rules[SchemaJsonKeys.Validations]);

        ApplyFieldRemovals(
            patch,
            clinicalFields,
            uiFields,
            rulesFields,
            layout);
        ApplyClinicalUpserts(patch, clinicalFields);
        ApplyUiUpserts(patch, uiFields);
        layout = ApplyLayoutReplace(patch, layout);
        ApplyRulesMutations(patch, rulesFields);
        ApplyValidationMutations(patch, validations);
        string schemaVersion = SchemaVersionOf(clinical);
        clinical[SchemaJsonKeys.Fields] = clinicalFields;
        ui[SchemaJsonKeys.SchemaVersion] = schemaVersion;
        ui[SchemaJsonKeys.ClinicalSchemaVersion] = schemaVersion;
        ui[SchemaJsonKeys.Fields] = uiFields;
        ui[SchemaJsonKeys.Layout] = layout;
        rules[SchemaJsonKeys.SchemaVersion] = schemaVersion;
        rules[SchemaJsonKeys.ClinicalSchemaVersion] = schemaVersion;
        rules[SchemaJsonKeys.Fields] = rulesFields;
        rules[SchemaJsonKeys.Validations] = validations;
        return new DraftTriple(clinical, ui, rules);
    }

    private static void ApplyFieldRemovals(
        JsonObject patch,
        JsonArray clinicalFields,
        JsonObject uiFields,
        JsonObject rulesFields,
        JsonArray layout)
    {
        foreach (string id in ReadStringArray(patch["removeFieldIds"]))
        {
            RemoveClinicalField(clinicalFields, id);
            _ = uiFields.Remove(id);
            _ = rulesFields.Remove(id);
            RemoveLayoutField(layout, id);
        }
    }

    private static void ApplyClinicalUpserts(
        JsonObject patch,
        JsonArray clinicalFields)
    {
        if (patch["upsertClinicalFields"] is not JsonArray upsertClinical)
        {
            return;
        }

        foreach (JsonNode? item in upsertClinical)
        {
            if (item is not JsonObject field
                || field[SchemaJsonKeys.Id]?.GetValue<string>() is not string id
                || string.IsNullOrWhiteSpace(id))
            {
                throw new ValidationException(
                    "patch.upsertClinicalFields entries must be objects with an id.");
            }

            UpsertArrayItem(clinicalFields, field, id);
        }
    }

    private static void ApplyUiUpserts(JsonObject patch, JsonObject uiFields)
    {
        if (patch["upsertUiFields"] is not JsonObject upsertUi)
        {
            return;
        }

        foreach ((string id, JsonNode? value) in upsertUi)
        {
            if (value is not JsonObject)
            {
                throw new ValidationException(
                    $"patch.upsertUiFields[\"{id}\"] must be an object.");
            }

            uiFields[id] = value.DeepClone();
        }
    }

    private static JsonArray ApplyLayoutReplace(
        JsonObject patch,
        JsonArray layout)
    {
        if (patch[SchemaJsonKeys.Layout] is not JsonNode layoutNode)
        {
            return layout;
        }

        if (layoutNode is not JsonArray layoutArray)
        {
            throw new ValidationException(
                "patch.layout must be an array when set.");
        }

        return CloneArray(layoutArray);
    }

    private static void ApplyRulesMutations(
        JsonObject patch,
        JsonObject rulesFields)
    {
        foreach (string id in ReadStringArray(patch["removeRulesFieldIds"]))
        {
            _ = rulesFields.Remove(id);
        }

        if (patch["upsertRulesFields"] is not JsonObject upsertRules)
        {
            return;
        }

        foreach ((string id, JsonNode? value) in upsertRules)
        {
            if (value is null)
            {
                _ = rulesFields.Remove(id);
            }
            else
            {
                rulesFields[id] = value is JsonObject
                    ? value.DeepClone()
                    : throw new ValidationException(
                        $"patch.upsertRulesFields[\"{id}\"] must be an object or null.");
            }
        }
    }

    private static void ApplyValidationMutations(
        JsonObject patch,
        JsonArray validations)
    {
        foreach (string code in ReadStringArray(patch["removeValidationCodes"]))
        {
            for (int index = validations.Count - 1; index >= 0; index--)
            {
                if (string.Equals(
                        validations[index]?[SchemaJsonKeys.Code]?.GetValue<string>(),
                        code,
                        StringComparison.Ordinal))
                {
                    validations.RemoveAt(index);
                }
            }
        }

        if (patch["upsertValidations"] is not JsonArray upsertValidations)
        {
            return;
        }

        foreach (JsonNode? item in upsertValidations)
        {
            if (item is not JsonObject validation
                || validation[SchemaJsonKeys.Code]?.GetValue<string>() is not string code
                || string.IsNullOrWhiteSpace(code))
            {
                throw new ValidationException(
                    "patch.upsertValidations entries must be objects with a code.");
            }

            UpsertArrayItem(validations, validation, code);
        }
    }

    private static void UpsertArrayItem(JsonArray items, JsonObject value, string key)
    {
        for (int index = 0; index < items.Count; index++)
        {
            if (string.Equals(
                    items[index]?[SchemaJsonKeys.Id]?.GetValue<string>(),
                    key,
                    StringComparison.Ordinal)
                || string.Equals(
                    items[index]?[SchemaJsonKeys.Code]?.GetValue<string>(),
                    key,
                    StringComparison.Ordinal))
            {
                items[index] = value.DeepClone();
                return;
            }
        }

        items.Add(value.DeepClone());
    }

    private static void RemoveClinicalField(JsonArray fields, string id)
    {
        for (int index = fields.Count - 1; index >= 0; index--)
        {
            var field = fields[index] as JsonObject;
            if (string.Equals(
                    field?[SchemaJsonKeys.Id]?.GetValue<string>(),
                    id,
                    StringComparison.Ordinal))
            {
                fields.RemoveAt(index);
                continue;
            }

            if (field?[SchemaJsonKeys.Items] is JsonArray children)
            {
                RemoveClinicalField(children, id);
            }
        }
    }

    private static void RemoveLayoutField(JsonArray nodes, string id)
    {
        for (int index = nodes.Count - 1; index >= 0; index--)
        {
            var node = nodes[index] as JsonObject;
            if (string.Equals(
                    node?[SchemaJsonKeys.Type]?.GetValue<string>(),
                    "field",
                    StringComparison.Ordinal)
                && string.Equals(
                    node?[SchemaJsonKeys.FieldId]?.GetValue<string>(),
                    id,
                    StringComparison.Ordinal))
            {
                nodes.RemoveAt(index);
                continue;
            }

            if (node?[SchemaJsonKeys.Children] is JsonArray children)
            {
                RemoveLayoutField(children, id);
            }

            if (node?["itemTemplate"] is JsonArray itemTemplate)
            {
                RemoveLayoutField(itemTemplate, id);
            }
        }
    }

    private static IEnumerable<string> ReadStringArray(JsonNode? node)
    {
        return node is JsonArray array
            ? array
                .Select(item => item?.GetValue<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!)
            : [];
    }

    private static JsonObject CloneObject(JsonNode? node)
    {
        return node is JsonObject value
            ? (JsonObject)value.DeepClone()
            : [];
    }

    private static JsonArray CloneArray(JsonNode? node)
    {
        return node is JsonArray value
            ? (JsonArray)value.DeepClone()
            : [];
    }

    private static string SchemaVersionOf(JsonObject clinical)
    {
        return clinical[SchemaJsonKeys.SchemaVersion]?.GetValue<string>() is string version
            && !string.IsNullOrWhiteSpace(version)
            ? version
            : "1.0.0";
    }
}

internal sealed record DraftTriple(
    JsonObject Clinical,
    JsonObject Ui,
    JsonObject Rules);
