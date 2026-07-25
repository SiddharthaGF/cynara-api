using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cynara.Application.Modules.FormAi;

public sealed partial class FormAiService
{
    private static JsonObject? ExtractJsonObject(string raw)
    {
        string content = UnwrapMarkdownJsonFence(raw.Trim()) ?? raw.Trim();

        try
        {
            return JsonNode.Parse(content) as JsonObject;
        }
        catch (JsonException)
        {
            return ExtractLastJsonObject(content);
        }
    }

    /// <summary>
    /// Prefers the last <c>```json</c> … <c>```</c> fence body when present so
    /// thinking prose outside the fence cannot poison parsing.
    /// </summary>
    private static string? UnwrapMarkdownJsonFence(string raw)
    {
        const string openFence = "```json";
        const string closeFence = "```";
        string? lastBody = ExtractLastJsonFenceBody(raw, openFence, closeFence);
        return lastBody ?? ExtractGenericFenceBody(raw, closeFence);
    }

    private static string? ExtractLastJsonFenceBody(string raw, string openFence, string closeFence)
    {
        string? lastBody = null;
        int searchFrom = 0;
        while (searchFrom < raw.Length)
        {
            int open = raw.IndexOf(openFence, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (open < 0)
            {
                break;
            }

            int close = FindFenceClose(raw, open, openFence, closeFence);
            if (close < 0)
            {
                break;
            }

            lastBody = ReadFenceBody(raw, open, openFence, close);
            searchFrom = close + closeFence.Length;
        }

        return lastBody;
    }

    private static int FindFenceClose(string raw, int open, string openFence, string closeFence)
    {
        int bodyStart = SkipNewlineAfterFence(raw, open + openFence.Length);
        return raw.IndexOf(closeFence, bodyStart, StringComparison.Ordinal);
    }

    private static string ReadFenceBody(string raw, int open, string openFence, int close)
    {
        int bodyStart = SkipNewlineAfterFence(raw, open + openFence.Length);
        return raw[bodyStart..close].Trim();
    }

    private static int SkipNewlineAfterFence(string raw, int start)
    {
        int bodyStart = start;
        if (bodyStart < raw.Length && raw[bodyStart] is '\r')
        {
            bodyStart++;
        }

        if (bodyStart < raw.Length && raw[bodyStart] is '\n')
        {
            bodyStart++;
        }

        return bodyStart;
    }

    private static string? ExtractGenericFenceBody(string raw, string closeFence)
    {
        if (!raw.StartsWith(closeFence, StringComparison.Ordinal)
            || !raw.EndsWith(closeFence, StringComparison.Ordinal)
            || raw.Length <= closeFence.Length * 2)
        {
            return null;
        }

        int firstNewline = raw.IndexOf('\n', StringComparison.Ordinal);
        return firstNewline >= 0
            ? raw[(firstNewline + 1)..^closeFence.Length].Trim()
            : raw[closeFence.Length..^closeFence.Length].Trim();
    }

    private static JsonObject? ExtractLastJsonObject(string content)
    {
        JsonObject? lastEnvelope = null;
        JsonObject? lastBarePatch = null;
        int current = 0;
        while (current < content.Length)
        {
            int open = content.IndexOf('{', current);
            if (open < 0)
            {
                break;
            }

            if (!TryParseBalancedJsonObject(content, open, out JsonObject? parsed)
                || parsed is null)
            {
                current = open + 1;
                continue;
            }

            current = FindBalancedObjectEnd(content, open) + 1;
            ClassifyParsedObject(parsed, ref lastEnvelope, ref lastBarePatch);
        }

        return lastEnvelope
            ?? (lastBarePatch is null ? null : WrapBarePatch(lastBarePatch));
    }

    private static void ClassifyParsedObject(
        JsonObject parsed,
        ref JsonObject? lastEnvelope,
        ref JsonObject? lastBarePatch)
    {
        if (LooksLikeAiResponseEnvelope(parsed))
        {
            lastEnvelope = parsed;
        }
        else if (LooksLikeBarePatch(parsed))
        {
            lastBarePatch = parsed;
        }
    }

    private static bool LooksLikeAiResponseEnvelope(JsonObject parsed)
    {
        return parsed["mode"] is not null
            || parsed["summary"] is not null
            || parsed["assistantMessage"] is not null
            || parsed["clinical"] is not null
            || parsed[AiModePatch] is not null
            || parsed["error"] is not null;
    }

    private static bool LooksLikeBarePatch(JsonObject parsed)
    {
        return parsed["upsertClinicalFields"] is not null
            || parsed["removeFieldIds"] is not null
            || parsed["upsertUiFields"] is not null
            || parsed[FormAiDraftPatch.LayerLayout] is not null
            || parsed["upsertRulesFields"] is not null
            || parsed["removeRulesFieldIds"] is not null
            || parsed["upsertValidations"] is not null
            || parsed["removeValidationCodes"] is not null
            || parsed["clear"] is not null;
    }

    private static JsonObject WrapBarePatch(JsonObject patch)
    {
        return new JsonObject
        {
            ["summary"] = "Updated form schemas.",
            ["assistantMessage"] = "Updated form schemas.",
            ["mode"] = AiModePatch,
            [AiModePatch] = patch.DeepClone(),
        };
    }

    private static bool TryParseBalancedJsonObject(
        string content,
        int start,
        out JsonObject? parsed)
    {
        parsed = null;
        int end = FindBalancedObjectEnd(content, start);
        if (end <= start)
        {
            return false;
        }

        return TryParseJsonSlice(content, start, end, out parsed);
    }

    private static bool TryParseJsonSlice(
        string content,
        int start,
        int end,
        out JsonObject? parsed)
    {
        try
        {
            parsed = JsonNode.Parse(content[start..(end + 1)]) as JsonObject;
            return parsed is not null;
        }
        catch (JsonException)
        {
            parsed = null;
            return false;
        }
    }

    private static int FindBalancedObjectEnd(string content, int start)
    {
        int depth = 0;
        bool inString = false;
        bool escaped = false;
        for (int index = start; index < content.Length; index++)
        {
            char character = content[index];
            if (inString)
            {
                (inString, escaped) = AdvanceInString(character, escaped);
                continue;
            }

            switch (character)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return index;
                    }

                    break;
                default:
                    // Any other character (whitespace, punctuation, content)
                    // leaves the bracket depth untouched.
                    break;
            }
        }

        return -1;
    }

    private static (bool InString, bool Escaped) AdvanceInString(char character, bool escaped)
    {
        if (escaped)
        {
            return (InString: true, Escaped: false);
        }

        if (character == '\\')
        {
            return (InString: true, Escaped: true);
        }

        if (character == '"')
        {
            return (InString: false, Escaped: false);
        }

        return (InString: true, Escaped: false);
    }

    private static JsonObject ParseObjectOrEmpty(string? json)
    {
        return string.IsNullOrWhiteSpace(json) ? [] : ExtractJsonObject(json) ?? [];
    }

    private static string? ReadText(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue(out string? text))
        {
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        return null;
    }
}
