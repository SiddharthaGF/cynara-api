using Cynara.Api.Hosting;

using Microsoft.OpenApi;

using Swashbuckle.AspNetCore.Swagger;

namespace Cynara.Api.JsonApi.OpenApi;

/// <summary>
/// Generates the Cynara OpenAPI document fully in-process — no host start,
/// no database connection. Single source of truth for the committed
/// <c>contracts/openapi.json</c> and the drift/determinism tests.
/// </summary>
public static class OpenApiDocumentExporter
{
    /// <summary>The Swagger document name registered by the API.</summary>
    public const string DocumentName = "v1";

    /// <summary>
    /// Generates and serializes the OpenAPI 3.0 document as compact JSON.
    /// Minified on purpose: a machine-consumed artifact for client
    /// generation and drift comparison.
    /// </summary>
    /// <remarks>
    /// Controllers are registered explicitly because the console-tool entry
    /// assembly differs; a dummy connection string satisfies EF registration;
    /// UseRouting plus an empty UseEndpoints populate EndpointDataSources
    /// because the exporter never starts the host.
    /// </remarks>
    /// <returns>The canonical OpenAPI document text.</returns>
    public static async Task<string> ExportAsync()
    {
        OpenApiDocumentSerializer.DisableRandomizedStringHashing();

        WebApplicationBuilder builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                Args = [],
                EnvironmentName = Environments.Development,
            });
        _ = builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:Default"] =
                    "Host=localhost;Database=cynara;Username=postgres",
            });

        _ = builder.Services
            .AddControllers()
            .AddApplicationPart(typeof(OpenApiDocumentExporter).Assembly);

        _ = builder.Services.AddCynaraApi(
            builder.Configuration,
            builder.Environment);
        await using WebApplication app = builder.Build();

        _ = app.MapMinimalApiEndpoints();

        _ = app.UseRouting();
        _ = app.UseEndpoints(_ => { });

        ISwaggerProvider provider = app.Services
            .GetRequiredService<ISwaggerProvider>();
        OpenApiDocument document = provider.GetSwagger(DocumentName);

        return OpenApiDocumentSerializer.Serialize(document);
    }
}
