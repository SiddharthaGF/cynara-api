using System.Globalization;

using Microsoft.OpenApi;

namespace Cynara.Api.JsonApi.OpenApi;

/// <summary>
/// Single source of truth for serializing the OpenAPI document as compact JSON
/// with the platform-workaround security transform applied. Both the committed
/// contract exporter and the live Development swagger endpoint use this, so
/// the served document and <c>contracts/openapi.json</c> can never diverge.
/// </summary>
public static class OpenApiDocumentSerializer
{
    /// <summary>
    /// Serializes <paramref name="document"/> to the canonical single-line,
    /// security-correct JSON used by the exporter and the live swagger endpoint.
    /// </summary>
    public static string Serialize(OpenApiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        DisableRandomizedStringHashing();

        using var textWriter = new StringWriter(CultureInfo.InvariantCulture);
        var settings = new OpenApiJsonWriterSettings { Terse = true };
        var jsonWriter = new OpenApiJsonWriter(textWriter, settings);
        document.SerializeAs(OpenApiSpecVersion.OpenApi3_0, jsonWriter);
        return OpenApiSecurityJsonTransform.Apply(textWriter.ToString());
    }

    /// <summary>
    /// Makes <see cref="string"/> hash ordering stable across processes so
    /// hash-backed collections (<c>required</c> sets, security scopes) render
    /// in the same order in the committed contract and the drift test. Must
    /// run before any document generation hashes strings.
    /// </summary>
    public static void DisableRandomizedStringHashing()
    {
        AppContext.SetSwitch(
            "System.Runtime.CompilerServices.UseRandomizedStringHash",
            isEnabled: false);
    }
}
