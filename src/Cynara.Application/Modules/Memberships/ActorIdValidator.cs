using System.Text.RegularExpressions;

namespace Cynara.Application.Modules.Memberships;

/// <summary>
/// Actor identity format rules (decision D3): starts alphanumeric,
/// continues with alphanumerics, hyphens, or underscores, at most 128
/// characters. Violations surface <see cref="ValidationException"/> 400;
/// a taken actor id is a conflict (409), never a format error.
/// </summary>
public static partial class ActorIdValidator
{
    public const int MaxLength = 128;

    [GeneratedRegex(
        "^[A-Za-z0-9][A-Za-z0-9_-]*$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex ActorIdPattern { get; }

    public static string RequireValid(string? actorId)
    {
        string normalized = actorId?.Trim() ?? string.Empty;
        if (normalized.Length == 0
            || normalized.Length > MaxLength
            || !ActorIdPattern.IsMatch(normalized))
        {
            throw new ValidationException(
                "actorId must start with a letter or digit, contain "
                + "only letters, digits, hyphens, or underscores, "
                + "and be at most 128 characters.");
        }

        return normalized;
    }
}
