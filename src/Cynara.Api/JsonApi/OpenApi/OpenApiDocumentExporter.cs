using System.Globalization;

using Cynara.Api.Hosting;

using Microsoft.OpenApi;

using Swashbuckle.AspNetCore.Swagger;

namespace Cynara.Api.JsonApi.OpenApi;

/// <summary>
/// Generates the Cynara OpenAPI document fully in-process, without starting
/// the host or opening a database connection. This is the single source of
/// truth for the committed <c>contracts/openapi.json</c> artifact and for the
/// drift/determinism tests, so the committed contract can never diverge from
/// what the live Development endpoint serves.
/// </summary>
public static class OpenApiDocumentExporter
{
    /// <summary>The Swagger document name registered by the API.</summary>
    public const string DocumentName = "v1";

    /// <summary>
    /// Generates and serializes the OpenAPI 3.0 document as compact JSON.
    /// The committed <c>contracts/openapi.json</c> is minified on purpose: it
    /// is a machine-consumed artifact for client generation and drift
    /// comparison, so a single line keeps the repository diff surface minimal
    /// compared to a ~25k-line indented document.
    /// </summary>
    /// <returns>The canonical OpenAPI document text.</returns>
    public static async Task<string> ExportAsync()
    {
        DisableRandomizedStringHashing();

        WebApplicationBuilder builder = WebApplication.CreateBuilder([]);
        _ = builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                // EF Core registration requires a connection string, but no
                // database is ever opened while the document is generated.
                // The values are dummies: the exporter never connects.
                ["ConnectionStrings:Default"] =
                    "Host=localhost;Database=cynara;Username=postgres",
            });

        // When the exporter runs from a console tool the entry assembly is
        // not Cynara.Api, so MVC must be told where the controllers live.
        _ = builder.Services
            .AddControllers()
            .AddApplicationPart(typeof(OpenApiDocumentExporter).Assembly);

        _ = builder.Services.AddCynaraApi(builder.Configuration);
        await using WebApplication app = builder.Build();

        ISwaggerProvider provider = app.Services
            .GetRequiredService<ISwaggerProvider>();
        OpenApiDocument document = provider.GetSwagger(DocumentName);

        await using var textWriter = new StringWriter(CultureInfo.InvariantCulture);
        var settings = new OpenApiJsonWriterSettings { Terse = true };
        var jsonWriter = new OpenApiJsonWriter(textWriter, settings);
        document.SerializeAs(OpenApiSpecVersion.OpenApi3_0, jsonWriter);
        return textWriter.ToString();
    }

    /// <summary>
    /// Makes <see cref="string"/> hash ordering stable across processes so
    /// hash-backed collections (<c>required</c> sets, security scopes) render
    /// in the same order in the committed contract and in the drift test.
    /// Must run before any document generation hashes strings.
    /// </summary>
    private static void DisableRandomizedStringHashing()
    {
        AppContext.SetSwitch(
            "System.Runtime.CompilerServices.UseRandomizedStringHash",
            isEnabled: false);
    }
}
