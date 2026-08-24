using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Cynara.Application.Modules.FormAi;

internal static partial class FormAiSanitizer
{
    [GeneratedRegex("-+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HyphenRegex { get; }

    [GeneratedRegex(
        "[^A-Z0-9]+",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex InvalidValidationCharactersRegex { get; }

    [GeneratedRegex(
        @"^\d+\.\d+\.\d+",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex SemverPrefixRegex { get; }

    private static IEnumerable<JsonObject> EnumerateFields(JsonArray? fields)
    {
        if (fields is null)
        {
            yield break;
        }

        foreach (JsonNode? value in fields)
        {
            if (value is not JsonObject field)
            {
                continue;
            }

            yield return field;
            if (field[SchemaJsonKeys.Items] is JsonArray children)
            {
                foreach (JsonObject child in EnumerateFields(children))
                {
                    yield return child;
                }
            }
        }
    }

    private static void CopyCommonClinical(JsonObject source, JsonObject result)
    {
        Copy(source, result, "required", "readOnly", "description", "default");
    }

    private static void Copy(
        JsonObject source,
        JsonObject result,
        params string[] keys)
    {
        foreach (string key in keys)
        {
            if (source[key] is JsonNode value)
            {
                result[key] = value.DeepClone();
            }
        }
    }

    private static JsonArray SanitizeOptions(JsonArray? input)
    {
        var options = new JsonArray();
        if (input is not null)
        {
            int index = 0;
            foreach (JsonNode? value in input)
            {
                if (value is not JsonObject option)
                {
                    continue;
                }

                string optionValue = option["value"]?.GetValue<string>()?.Trim()
                    ?? string.Create(
                        CultureInfo.InvariantCulture,
                        $"option-{++index}");
                string label =
                    option[SchemaJsonKeys.Label]?.GetValue<string>()?.Trim()
                    ?? optionValue;
                options.Add(new JsonObject
                {
                    ["value"] =
                        optionValue[..Math.Min(optionValue.Length, 128)],
                    [SchemaJsonKeys.Label] =
                        label[..Math.Min(label.Length, 256)],
                });
            }
        }

        if (options.Count == 0)
        {
            options.Add(new JsonObject
            {
                ["value"] = "option-1",
                [SchemaJsonKeys.Label] = "Option 1",
            });
        }

        return options;
    }

    private static string UniqueId(string id, HashSet<string> used)
    {
        if (!used.Contains(id))
        {
            return id;
        }

        for (int suffix = 2; suffix < 1000; suffix++)
        {
            string candidate = string.Create(
                CultureInfo.InvariantCulture,
                $"{id}-{suffix}");
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{id}-x";
    }

    private static string SlugifyKebab(string value)
    {
        string normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (char character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character)
                == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            _ = builder.Append(
                char.IsLetterOrDigit(character)
                    ? char.ToLowerInvariant(character)
                    : '-');
        }

        string result = HyphenRegex.Replace(builder.ToString(), "-").Trim('-');
        return result.Length > 0 && char.IsLetter(result[0])
            ? result[..Math.Min(result.Length, 64)]
            : "field";
    }

    private static string SlugifyCode(string value)
    {
        string result = SlugifyKebab(value).Replace('-', '.');
        return result[..Math.Min(result.Length, 128)];
    }

    private static string ToValidationCode(string? raw, string fallback)
    {
        string source = string.IsNullOrWhiteSpace(raw) ? fallback : raw;
        string code = InvalidValidationCharactersRegex
            .Replace(source.ToUpperInvariant(), "_")
            .Trim('_');
        if (code.Length == 0 || !char.IsLetter(code[0]))
        {
            code = $"V_{code}";
        }

        code = code[..Math.Min(code.Length, 64)];
        return code.Length >= 3 ? code : fallback;
    }

    private static string? AsSemver(string? value)
    {
        if (value is null || !SemverPrefixRegex.IsMatch(value))
        {
            return null;
        }

        return value.Split(['-', '+'], count: 2)[0];
    }

    private static JsonObject AsObject(JsonNode? node)
    {
        return node is JsonObject value ? value : [];
    }

    private static string Humanize(string id)
    {
        string text = id.Replace('-', ' ').Trim();
        return text.Length == 0
            ? "Field"
            : char.ToUpperInvariant(text[0]) + text[1..];
    }
}

internal sealed record SanitizedAiTriple(
    JsonObject Clinical,
    JsonObject Ui,
    JsonObject Rules);
