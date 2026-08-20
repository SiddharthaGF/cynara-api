using System.Text;

namespace Cynara.Api.JsonApi.OpenApi;

/// <summary>
/// Restores security requirement scheme names that Microsoft.OpenApi 2.4.1
/// drops when serializing programmatically generated documents. The framework's
/// requirement serializer skips every key whose <c>Target</c> is unresolved and
/// writes only the resolved name, so generated requirements render as empty
/// objects (<c>[{}]</c>). This transform rewrites each operation-level
/// <c>security</c> array to its canonical AND-ed bearer + hospital shape while
/// leaving every other byte of the document untouched. The single
/// tenant-exempt exception is the membership listing
/// (<c>GET /api/me/hospitals</c>), whose degenerate requirement rewrites to
/// the bearer-only shape so the contract never advertises the hospital header
/// for the one authenticated route that must not require it.
/// </summary>
internal static class OpenApiSecurityJsonTransform
{
    private const string CanonicalSecurityValue =
        "[{\"" + OpenApiSecurity.Bearer + "\":[],\""
        + OpenApiSecurity.HospitalCode + "\":[]}]";

    private const string BearerOnlySecurityValue =
        "[{\"" + OpenApiSecurity.Bearer + "\":[]}]";

    /// <summary>
    /// Returns <paramref name="json"/> with every degenerate per-operation
    /// <c>security</c> requirement replaced by the canonical bearer + hospital
    /// requirement, except under the tenant-exempt membership listing, which
    /// becomes bearer-only. Arrays that already carry named schemes are left
    /// alone.
    /// </summary>
    public static string Apply(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var builder = new StringBuilder(json);
        var edits = new List<Edit>();

        // Object keys of every currently open JSON object, outermost first.
        // The entry directly above the `paths` container names the route path
        // being described, which decides how a degenerate security array is
        // rewritten.
        var contexts = new Stack<string>();

        for (int i = 0; i < builder.Length; i++)
        {
            char current = builder[i];
            if (current == '"')
            {
                i = ConsumeValueOrKey(builder, i, contexts, edits);
                continue;
            }

            if ((current is '}' or ']')
                && contexts.Count > 0)
            {
                _ = contexts.Pop();
            }
        }

        // Apply from the end so earlier edit indices stay valid.
        for (int edit = edits.Count - 1; edit >= 0; edit--)
        {
            builder.Remove(edits[edit].Start, edits[edit].Length)
                .Insert(edits[edit].Start, edits[edit].Value);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Handles a quoted token that starts at <paramref name="openQuoteIndex"/>.
    /// Returns the index of its closing quote when it is a string value, or the
    /// index just past the value when it is a key with a known value kind.
    /// </summary>
    private static int ConsumeValueOrKey(
        StringBuilder builder,
        int openQuoteIndex,
        Stack<string> contexts,
        List<Edit> edits)
    {
        int keyEnd = SkipString(builder, openQuoteIndex);
        int colonIndex = keyEnd + 1;
        while (colonIndex < builder.Length
            && char.IsWhiteSpace(builder[colonIndex]))
        {
            colonIndex++;
        }

        if (colonIndex >= builder.Length || builder[colonIndex] != ':')
        {
            // A string value, not a key.
            return keyEnd;
        }

        string key = builder.ToString(
            openQuoteIndex + 1,
            keyEnd - openQuoteIndex - 1);
        int valueStart = colonIndex + 1;
        while (valueStart < builder.Length
            && char.IsWhiteSpace(builder[valueStart]))
        {
            valueStart++;
        }

        if (valueStart >= builder.Length)
        {
            return keyEnd;
        }

        char valueOpen = builder[valueStart];
        if (string.Equals(key, "security", StringComparison.Ordinal)
            && valueOpen == '[')
        {
            int valueEnd = FindMatchingBracket(builder, valueStart);
            if (valueEnd >= 0
                && IsDegenerateSecurityArray(builder, valueStart, valueEnd))
            {
                edits.Add(new Edit(
                    valueStart,
                    valueEnd - valueStart + 1,
                    RewriteFor(CurrentPath(contexts))));
            }

            return valueEnd >= 0 ? valueEnd : keyEnd;
        }

        if (valueOpen == '{')
        {
            contexts.Push(key);
            return valueStart - 1;
        }

        if (valueOpen == '[')
        {
            int valueEnd = FindMatchingBracket(builder, valueStart);
            return valueEnd >= 0 ? valueEnd : keyEnd;
        }

        if (valueOpen == '"')
        {
            return SkipString(builder, valueStart);
        }

        // Scalar value (number, boolean, null): skip to the next delimiter.
        int index = valueStart;
        while (index < builder.Length
            && builder[index] is not (',' or '}' or ']'))
        {
            index++;
        }

        return index - 1;
    }

    /// <summary>
    /// Returns the route path of the operation currently being scanned, or
    /// <see langword="null"/> when the scan is not inside the <c>paths</c>
    /// container (for example document-level security).
    /// </summary>
    private static string? CurrentPath(Stack<string> contexts)
    {
        string[] items = [.. contexts];
        for (int index = items.Length - 1; index >= 0; index--)
        {
            if (string.Equals(items[index], "paths", StringComparison.Ordinal))
            {
                // Stack.ToArray yields top-first (LIFO), so the entry pushed
                // right after `paths` — the route path key — sits below it.
                return index - 1 >= 0 ? items[index - 1] : null;
            }
        }

        return null;
    }

    private static string RewriteFor(string? path)
    {
        return OpenApiSecurity.IsTenantExemptPath(path)
            ? BearerOnlySecurityValue
            : CanonicalSecurityValue;
    }

    /// <summary>
    /// A degenerate security array contains only empty requirement objects
    /// (<c>[{}]</c>, <c>[{},{}]</c>): no quoted scheme names anywhere. Arrays
    /// that already carry scheme names are left untouched.
    /// </summary>
    private static bool IsDegenerateSecurityArray(
        StringBuilder builder,
        int startInclusive,
        int endInclusive)
    {
        for (int i = startInclusive; i <= endInclusive; i++)
        {
            char c = builder[i];
            if (c is '{' or '}' or '[' or ']' or ',')
            {
                continue;
            }

            if (c == '"')
            {
                return false;
            }

            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static int SkipString(StringBuilder builder, int openQuoteIndex)
    {
        for (int i = openQuoteIndex + 1; i < builder.Length; i++)
        {
            if (builder[i] == '\\')
            {
                i++;
                continue;
            }

            if (builder[i] == '"')
            {
                return i;
            }
        }

        return builder.Length - 1;
    }

    private static int FindMatchingBracket(StringBuilder builder, int openIndex)
    {
        int depth = 0;
        for (int i = openIndex; i < builder.Length; i++)
        {
            char c = builder[i];
            if (c == '"')
            {
                i = SkipString(builder, i);
                continue;
            }

            if (c == '[')
            {
                depth++;
            }
            else if (c == ']' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private readonly record struct Edit(int Start, int Length, string Value);
}
