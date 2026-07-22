using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cynara.Application.Common;

internal static class ContentHashCalculator
{
    public static string Compute(string clinicalSchemaJson, string? uiSchemaJson, string? rulesSchemaJson = null)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(BuildCanonicalPayload(clinicalSchemaJson, uiSchemaJson, rulesSchemaJson));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static string BuildCanonicalPayload(string clinicalSchemaJson, string? uiSchemaJson, string? rulesSchemaJson)
    {
        using var clinical = JsonDocument.Parse(clinicalSchemaJson);
        using JsonDocument? ui = uiSchemaJson is null ? null : JsonDocument.Parse(uiSchemaJson);
        using JsonDocument? rules = rulesSchemaJson is null ? null : JsonDocument.Parse(rulesSchemaJson);

        var payload = new Dictionary<string, JsonElement?>
        {
            ["clinical"] = clinical.RootElement.Clone(),
            ["ui"] = ui?.RootElement.Clone(),
            ["rules"] = rules?.RootElement.Clone(),
        };

        return JsonSerializer.Serialize(payload, CanonicalJsonOptions.Instance);
    }
}

internal static class SemverRules
{
    public static IComparer<string> StringComparer { get; } = Comparer<string>.Create(
        (left, right) => Compare(Parse(left), Parse(right)));

    public static string NextVersion(IEnumerable<string> publishedVersions)
    {
        string? latest = publishedVersions
            .OrderBy(static version => version, StringComparer)
            .LastOrDefault();

        if (latest is null)
        {
            return "1.0.0";
        }

        VersionParts parts = Parse(latest);
        return $"{parts.Major}.{parts.Minor}.{parts.Patch + 1}";
    }

    public static void EnsureValid(string version)
    {
        _ = Parse(version);
    }

    private static VersionParts Parse(string version)
    {
        string[] segments = version.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length != 3
            || !int.TryParse(segments[0], out int major)
            || !int.TryParse(segments[1], out int minor)
            || !int.TryParse(segments[2], out int patch)
            || major < 0
            || minor < 0
            || patch < 0
            ? throw new ValidationException($"Invalid semantic version: {version}")
            : new VersionParts(major, minor, patch);
    }

    private static int Compare(VersionParts left, VersionParts right)
    {
        int major = left.Major.CompareTo(right.Major);
        if (major != 0)
        {
            return major;
        }

        int minor = left.Minor.CompareTo(right.Minor);
        return minor != 0 ? minor : left.Patch.CompareTo(right.Patch);
    }

    private readonly record struct VersionParts(int Major, int Minor, int Patch);
}

internal static class CanonicalJsonOptions
{
    public static JsonSerializerOptions Instance { get; } = new()
    {
        WriteIndented = false,
    };
}
