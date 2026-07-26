using System.Text.RegularExpressions;

namespace Cynara.Application.Common;

internal static partial class CodeRules
{
    private const string Pattern = "^[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*$";

    [GeneratedRegex(Pattern, RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MyRegex { get; }

    public static void EnsureValid(string code, string entityKind)
    {
        if (string.IsNullOrWhiteSpace(code)
            || code.Length > 128
            || !MyRegex.IsMatch(code))
        {
            throw new ValidationException(
                $"{entityKind} code must match pattern ^[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*$ and be at most 128 characters.");
        }
    }
}
