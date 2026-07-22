using System.Text.RegularExpressions;

namespace Cynara.Application.Components;

internal static partial class ComponentCodeRules
{
    private const string Pattern = "^[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*$";

    public static void EnsureValid(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 128 || !MyRegex().IsMatch(code))
        {
            throw new ValidationException(
                "Component code must match pattern ^[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*$ and be at most 128 characters.");
        }
    }

    [GeneratedRegex(Pattern)]
    private static partial Regex MyRegex();
}
