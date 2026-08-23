using System.Text;

namespace Cynara.Api.JsonApi.OpenApi;

/// <summary>
/// Repairs per-operation <c>security</c> requirements that Microsoft.OpenApi
/// serializes as empty objects (<c>[{}]</c>) for programmatically generated
/// documents, rewriting them in place to their canonical scheme shapes.
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
    /// requirement; tenant-exempt paths become bearer-only and arrays that
    /// already carry scheme names are left alone.
    /// </summary>
    /// <remarks>
    /// Open object keys are tracked on a stack — the entry pushed directly
    /// above <c>paths</c> names the route path being described — and recorded
    /// edits apply from the end so earlier edit indices stay valid.
    /// </remarks>
    public static string Apply(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var builder = new StringBuilder(json);
        var edits = new List<Edit>();

        var contexts = new Stack<string>();

        int i = 0;
        while (i < builder.Length)
        {
            char current = builder[i];
            if (current == '"')
            {
                i = ConsumeValueOrKey(builder, i, contexts, edits) + 1;
                continue;
            }

            if ((current is '}' or ']')
                && contexts.Count > 0)
            {
                _ = contexts.Pop();
            }

            i++;
        }

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
        int colonIndex = SkipWhitespace(builder, keyEnd + 1);

        if (colonIndex >= builder.Length || builder[colonIndex] != ':')
        {
            return keyEnd;
        }

        string key = builder.ToString(
            openQuoteIndex + 1,
            keyEnd - openQuoteIndex - 1);
        int valueStart = SkipWhitespace(builder, colonIndex + 1);

        if (valueStart >= builder.Length)
        {
            return keyEnd;
        }

        char valueOpen = builder[valueStart];
        if (string.Equals(key, "security", StringComparison.Ordinal)
            && valueOpen == '[')
        {
            return HandleSecurityValue(
                builder,
                valueStart,
                keyEnd,
                contexts,
                edits);
        }

        return SkipValue(builder, valueStart, valueOpen, key, keyEnd, contexts);
    }

    /// <summary>Returns the first index at or after <paramref name="from"/> holding a non-whitespace character.</summary>
    private static int SkipWhitespace(StringBuilder builder, int from)
    {
        int index = from;
        while (index < builder.Length
            && char.IsWhiteSpace(builder[index]))
        {
            index++;
        }

        return index;
    }

    /// <summary>
    /// Records a canonical rewrite when the security array starting at
    /// <paramref name="valueStart"/> is degenerate; returns the index scan
    /// should resume from.
    /// </summary>
    private static int HandleSecurityValue(
        StringBuilder builder,
        int valueStart,
        int keyEnd,
        Stack<string> contexts,
        List<Edit> edits)
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

    /// <summary>
    /// Skips over a non-security value according to its opening shape;
    /// scalars (number, boolean, null) scan to the next delimiter.
    /// </summary>
    private static int SkipValue(
        StringBuilder builder,
        int valueStart,
        char valueOpen,
        string key,
        int keyEnd,
        Stack<string> contexts)
    {
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

        int index = valueStart;
        while (index < builder.Length
            && builder[index] is not (',' or '}' or ']'))
        {
            index++;
        }

        return index - 1;
    }

    /// <summary>
    /// Returns the route path of the operation being scanned, or
    /// <see langword="null"/> outside the <c>paths</c> container. Stack.ToArray
    /// is top-first (LIFO), so the entry pushed right after <c>paths</c> — the
    /// route path key — sits below it.
    /// </summary>
    private static string? CurrentPath(Stack<string> contexts)
    {
        string[] items = [.. contexts];
        for (int index = items.Length - 1; index >= 0; index--)
        {
            if (string.Equals(items[index], "paths", StringComparison.Ordinal))
            {
                return index > 0 ? items[index - 1] : null;
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

    /// <summary>
    /// Returns the index of the closing quote; escape pairs advance past the
    /// escaped character too.
    /// </summary>
    private static int SkipString(StringBuilder builder, int openQuoteIndex)
    {
        int index = openQuoteIndex + 1;
        while (index < builder.Length)
        {
            if (builder[index] == '\\')
            {
                index += 2;
                continue;
            }

            if (builder[index] == '"')
            {
                return index;
            }

            index++;
        }

        return builder.Length - 1;
    }

    private static int FindMatchingBracket(StringBuilder builder, int openIndex)
    {
        int depth = 0;
        int i = openIndex;
        while (i < builder.Length)
        {
            char c = builder[i];
            if (c == '"')
            {
                i = SkipString(builder, i) + 1;
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

            i++;
        }

        return -1;
    }

    private readonly record struct Edit(int Start, int Length, string Value);
}
