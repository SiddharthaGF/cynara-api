using System.ComponentModel.DataAnnotations;

namespace Cynara.Domain.Common;

/// <summary>
/// Shared validation rules for tenant-owned business codes (hospital,
/// facility, clinical area, discipline). <see cref="EnsureValid"/> enforces
/// bounds only; the regex <see cref="Pattern"/> documents the OpenAPI/EF
/// constraint and is intentionally not evaluated at runtime.
/// </summary>
public static class ResourceCodeRules
{
    public const int MinLength = 1;

    public const int MaxLength = 64;

    public const string Pattern =
        "^[a-zA-Z0-9][a-zA-Z0-9._-]{0,62}[a-zA-Z0-9]$";

    public static void EnsureValid(string? code, string entityName)
    {
        if (string.IsNullOrWhiteSpace(code)
            || code.Length < MinLength
            || code.Length > MaxLength)
        {
            throw new ValidationException(
                $"{entityName} code '{code}' must be {MinLength}-{MaxLength} characters.");
        }
    }
}
