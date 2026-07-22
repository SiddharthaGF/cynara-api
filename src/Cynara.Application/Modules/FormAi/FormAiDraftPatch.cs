using System.Text.Json.Nodes;

namespace Cynara.Application.Modules.FormAi;

internal static class FormAiDraftPatch
{
    public static DraftTriple Empty(string schemaVersion = "1.0.0")
    {
        return new DraftTriple(
            new JsonObject
            {
                ["schemaVersion"] = schemaVersion,
                ["fields"] = new JsonArray(),
            },
            new JsonObject
            {
                ["schemaVersion"] = schemaVersion,
                ["clinicalSchemaVersion"] = schemaVersion,
                ["fields"] = new JsonObject(),
                ["layout"] = new JsonArray(),
            },
            new JsonObject
            {
                ["schemaVersion"] = schemaVersion,
                ["clinicalSchemaVersion"] = schemaVersion,
                ["fields"] = new JsonObject(),
                ["validations"] = new JsonArray(),
            });
    }

    public static DraftTriple Apply(DraftTriple baseTriple, JsonNode patchNode)
    {
        if (patchNode is not JsonObject patch)
        {
            throw new ValidationException("AI patch response must include a patch object.");
        }

        if (patch["clear"]?.GetValue<bool>() is true)
        {
            return Empty(SchemaVersionOf(baseTriple.Clinical));
        }

        JsonObject clinical = CloneObject(baseTriple.Clinical);
        JsonObject ui = CloneObject(baseTriple.Ui);
        JsonObject rules = CloneObject(baseTriple.Rules);
        JsonArray clinicalFields = CloneArray(clinical["fields"]);
        JsonObject uiFields = CloneObject(ui["fields"]);
        JsonObject rulesFields = CloneObject(rules["fields"]);
        JsonArray layout = CloneArray(ui["layout"]);
        JsonArray validations = CloneArray(rules["validations"]);

        foreach (string id in ReadStringArray(patch["removeFieldIds"]))
        {
            RemoveClinicalField(clinicalFields, id);
            _ = uiFields.Remove(id);
            _ = rulesFields.Remove(id);
            RemoveLayoutField(layout, id);
        }

        if (patch["upsertClinicalFields"] is JsonArray upsertClinical)
        {
            foreach (JsonNode? item in upsertClinical)
            {
                if (item is not JsonObject field
                    || field["id"]?.GetValue<string>() is not string id
                    || string.IsNullOrWhiteSpace(id))
                {
                    throw new ValidationException(
                        "patch.upsertClinicalFields entries must be objects with an id.");
                }

                UpsertArrayItem(clinicalFields, field, id);
            }
        }

        if (patch["upsertUiFields"] is JsonObject upsertUi)
        {
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

        if (patch["layout"] is JsonNode layoutNode)
        {
            if (layoutNode is not JsonArray layoutArray)
            {
                throw new ValidationException("patch.layout must be an array when set.");
            }

            layout = CloneArray(layoutArray);
        }

        foreach (string id in ReadStringArray(patch["removeRulesFieldIds"]))
        {
            _ = rulesFields.Remove(id);
        }

        if (patch["upsertRulesFields"] is JsonObject upsertRules)
        {
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

        foreach (string code in ReadStringArray(patch["removeValidationCodes"]))
        {
            for (int index = validations.Count - 1; index >= 0; index--)
            {
                if (validations[index]?["code"]?.GetValue<string>() == code)
                {
                    validations.RemoveAt(index);
                }
            }
        }

        if (patch["upsertValidations"] is JsonArray upsertValidations)
        {
            foreach (JsonNode? item in upsertValidations)
            {
                if (item is not JsonObject validation
                    || validation["code"]?.GetValue<string>() is not string code
                    || string.IsNullOrWhiteSpace(code))
                {
                    throw new ValidationException(
                        "patch.upsertValidations entries must be objects with a code.");
                }

                UpsertArrayItem(validations, validation, code);
            }
        }

        string schemaVersion = SchemaVersionOf(clinical);
        clinical["fields"] = clinicalFields;
        ui["schemaVersion"] = schemaVersion;
        ui["clinicalSchemaVersion"] = schemaVersion;
        ui["fields"] = uiFields;
        ui["layout"] = layout;
        rules["schemaVersion"] = schemaVersion;
        rules["clinicalSchemaVersion"] = schemaVersion;
        rules["fields"] = rulesFields;
        rules["validations"] = validations;
        return new DraftTriple(clinical, ui, rules);
    }

    private static void UpsertArrayItem(JsonArray items, JsonObject value, string key)
    {
        for (int index = 0; index < items.Count; index++)
        {
            if (items[index]?["id"]?.GetValue<string>() == key
                || items[index]?["code"]?.GetValue<string>() == key)
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
            if (field?["id"]?.GetValue<string>() == id)
            {
                fields.RemoveAt(index);
                continue;
            }

            if (field?["items"] is JsonArray children)
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
            if (node?["type"]?.GetValue<string>() == "field"
                && node["fieldId"]?.GetValue<string>() == id)
            {
                nodes.RemoveAt(index);
                continue;
            }

            if (node?["children"] is JsonArray children)
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
        return clinical["schemaVersion"]?.GetValue<string>() is string version
            && !string.IsNullOrWhiteSpace(version)
            ? version
            : "1.0.0";
    }
}

internal sealed record DraftTriple(
    JsonObject Clinical,
    JsonObject Ui,
    JsonObject Rules);
