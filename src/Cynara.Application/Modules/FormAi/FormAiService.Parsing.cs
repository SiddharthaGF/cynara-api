using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Cynara.Application.Common;

namespace Cynara.Application.Modules.FormAi;

public sealed partial class FormAiService
{
    private static ParsedAiOutput ParseModelOutput(
        string raw,
        DraftContext draft,
        string locale)
    {
        JsonObject parsed = ExtractJsonObject(raw)
            ?? throw new ValidationException(
                "AI response was not valid JSON. Ask again or simplify the requirement.");
        string summary = ReadText(parsed["summary"]) ?? "Updated form schemas.";
        string assistantMessage = ReadText(parsed["assistantMessage"]) ?? summary;
        JsonNode? error = parsed["error"];
        if (error is JsonObject errorObject)
        {
            string message = ReadText(errorObject[SchemaJsonKeys.Message])
                ?? FormAiGuardrails.LimitationMessage(FormAiGuardCode.OutOfScope, locale);
            return ParsedAiOutput.Unchanged(
                FormAiGuardrails.LimitationSummary(locale),
                message,
                draft,
                isRefusal: true);
        }

        string mode = ReadText(parsed["mode"])?.ToLowerInvariant() ?? ResolveMode(parsed);
        if (string.Equals(mode, "unchanged", StringComparison.Ordinal))
        {
            return ParsedAiOutput.Unchanged(
                summary,
                assistantMessage,
                draft,
                isRefusal: false);
        }

        if (string.Equals(mode, AiModePatch, StringComparison.Ordinal))
        {
            if (parsed[AiModePatch] is not JsonNode patch)
            {
                throw new ValidationException("AI patch response must include a patch object.");
            }

            DraftTriple baseTriple = ParseDraftTriple(draft);
            DraftTriple patched = FormAiDraftPatch.Apply(baseTriple, patch);
            return new ParsedAiOutput(
                summary,
                assistantMessage,
                patched,
                LimitationOnly: false);
        }

        if (string.Equals(mode, "replace", StringComparison.Ordinal))
        {
            if (parsed["clinical"] is not JsonObject clinical
                || parsed["ui"] is not JsonObject ui
                || parsed["rules"] is not JsonObject rules)
            {
                throw new ValidationException(
                    "AI replace response must include clinical, ui, and rules objects.");
            }

            return new ParsedAiOutput(
                summary,
                assistantMessage,
                new DraftTriple(
                    (JsonObject)clinical.DeepClone(),
                    (JsonObject)ui.DeepClone(),
                    (JsonObject)rules.DeepClone()),
                LimitationOnly: false);
        }

        throw new ValidationException(
            "AI response must set mode to unchanged, patch, or replace.");
    }

    private static FocusContext BuildFocusContext(
        FormAiChatRequest request,
        string latestMessage,
        DraftContext draft)
    {
        List<string> ids = CollectFocusedFieldIds(request, latestMessage);
        JsonObject clinical = ParseObjectOrEmpty(draft.ClinicalSchemaJson);
        JsonObject ui = ParseObjectOrEmpty(draft.UiSchemaJson);
        JsonObject rules = ParseObjectOrEmpty(draft.RulesSchemaJson);
        IReadOnlyList<FocusedField> fields = ResolveFocusedFields(ids, clinical, ui, rules);
        IReadOnlyList<FocusedFieldType> types = CollectFocusedFieldTypes(request, latestMessage);
        return new FocusContext(fields, types);
    }

    private static List<string> CollectFocusedFieldIds(
        FormAiChatRequest request,
        string latestMessage)
    {
        var ids = new List<string>();
        if (request.FocusedFieldIds is not null)
        {
            ids.AddRange(request.FocusedFieldIds.Where(IsValidFieldId));
        }

        foreach (Match match in FieldMentionRegex.Matches(latestMessage))
        {
            string id = match.Groups["id"].Value;
            if (IsValidFieldId(id))
            {
                ids.Add(id);
            }
        }

        return [.. ids.Distinct(StringComparer.Ordinal).Take(MaxFocusedFields)];
    }

    private static List<FocusedField> ResolveFocusedFields(
        IReadOnlyList<string> ids,
        JsonObject clinical,
        JsonObject ui,
        JsonObject rules)
    {
        var byId = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        IndexFields(clinical[SchemaJsonKeys.Fields] as JsonArray, byId);
        var fields = new List<FocusedField>();
        foreach (string id in ids)
        {
            if (byId.TryGetValue(id, out JsonNode? field))
            {
                fields.Add(new FocusedField(
                    id,
                    field.DeepClone(),
                    ui[SchemaJsonKeys.Fields]?[id]?.DeepClone(),
                    rules[SchemaJsonKeys.Fields]?[id]?.DeepClone()));
            }
        }

        return fields;
    }

    private static List<FocusedFieldType> CollectFocusedFieldTypes(
        FormAiChatRequest request,
        string latestMessage)
    {
        var types = new List<FocusedFieldType>();
        foreach (Match match in TypeMentionRegex.Matches(latestMessage))
        {
            string type = match.Groups["type"].Value.ToUpperInvariant();
            if (type.Length > 0 && types.TrueForAll(item => !string.Equals(item.Type, type, StringComparison.Ordinal)))
            {
                types.Add(new FocusedFieldType(type, []));
            }
        }

        if (request.FocusedFieldTypes is not null)
        {
            foreach (string type in request.FocusedFieldTypes.Where(item => !string.IsNullOrWhiteSpace(item)))
            {
                string normalized = type.Trim().ToUpperInvariant();
                if (types.TrueForAll(item => !string.Equals(item.Type, normalized, StringComparison.Ordinal)))
                {
                    types.Add(new FocusedFieldType(normalized, []));
                }
            }
        }

        return types;
    }

    private static void IndexFields(JsonArray? fields, Dictionary<string, JsonNode> result)
    {
        if (fields is null)
        {
            return;
        }

        foreach (JsonNode? node in fields)
        {
            if (node is not JsonObject field
                || field[SchemaJsonKeys.Id]?.GetValue<string>() is not string id)
            {
                continue;
            }

            result[id] = field;
            IndexFields(field[SchemaJsonKeys.Items] as JsonArray, result);
        }
    }

    private static async Task WriteSseAsync(
        Stream output,
        object payload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(payload);
        byte[] prefix = Encoding.UTF8.GetBytes("data: ");
        byte[] suffix = Encoding.UTF8.GetBytes("\n\n");
        byte[] message = new byte[prefix.Length + json.Length + suffix.Length];
        Buffer.BlockCopy(prefix, 0, message, 0, prefix.Length);
        Buffer.BlockCopy(json, 0, message, prefix.Length, json.Length);
        Buffer.BlockCopy(suffix, 0, message, prefix.Length + json.Length, suffix.Length);
        await output.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static PartialStringField? ExtractPartialJsonStringField(string buffer, string field)
    {
        int? valueStart = FindPartialJsonStringValueStart(buffer, field);
        return valueStart is null
            ? null
            : DecodePartialJsonStringValue(buffer, valueStart.Value);
    }

    private static int? FindPartialJsonStringValueStart(string buffer, string field)
    {
        string key = $"\"{field}\"";
        int keyIndex = buffer.IndexOf(key, StringComparison.Ordinal);
        if (keyIndex < 0)
        {
            return null;
        }

        int colon = buffer.IndexOf(':', keyIndex + key.Length);
        if (colon < 0)
        {
            return null;
        }

        int index = colon + 1;
        while (index < buffer.Length && char.IsWhiteSpace(buffer[index]))
        {
            index++;
        }

        if (index >= buffer.Length || buffer[index] != '"')
        {
            return null;
        }

        return index + 1;
    }

    private static PartialStringField DecodePartialJsonStringValue(string buffer, int index)
    {
        var value = new StringBuilder();
        bool escaped = false;
        for (; index < buffer.Length; index++)
        {
            char character = buffer[index];
            if (escaped)
            {
                _ = value.Append(character switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    _ => character,
                });
                escaped = false;
                continue;
            }

            if (character == '\\')
            {
                escaped = true;
                continue;
            }

            if (character == '"')
            {
                return new PartialStringField(value.ToString(), Complete: true);
            }

            _ = value.Append(character);
        }

        return new PartialStringField(value.ToString(), Complete: false);
    }
}
