using System.Globalization;
using System.Runtime.InteropServices;
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

        Dictionary<string, JsonElement?> payload = new(StringComparer.Ordinal)
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
    private const string DefaultVersion = "1.0.0";

    public static IComparer<string> StringComparer { get; } = Comparer<string>.Create(
        (left, right) => Compare(Parse(left), Parse(right)));

    public static string NextVersion(IEnumerable<string> publishedVersions)
    {
        string? latest = publishedVersions
            .Order(StringComparer)
            .LastOrDefault();

        if (latest is null)
        {
            return DefaultVersion;
        }

        VersionParts parts = Parse(latest);
        return string.Create(CultureInfo.InvariantCulture, $"{parts.Major}.{parts.Minor}.{parts.Patch + 1}");
    }

    public static void EnsureValid(string version)
    {
        _ = Parse(version);
    }

    private static VersionParts Parse(string version)
    {
        string[] segments = version.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length != 3
            || !int.TryParse(segments[0], CultureInfo.InvariantCulture, out int major) || !int.TryParse(segments[1], CultureInfo.InvariantCulture, out int minor) || !int.TryParse(segments[2], CultureInfo.InvariantCulture, out int patch) || major < 0
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

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct VersionParts(int Major, int Minor, int Patch);
}

internal static class CanonicalJsonOptions
{
    public static JsonSerializerOptions Instance { get; } = new()
    {
        WriteIndented = false,
    };
}
