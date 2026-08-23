using Cynara.Infrastructure.Schemas;

namespace Cynara.Api.Modules.Schemas;

/// <summary>
/// Serves the versioned clinical schema contract over HTTP so any client
/// (cynara-web, external tooling) validates against the same meta-schemas the
/// API uses at runtime. The contract documents are read from the embedded
/// <c>Schemas/v1</c> output directory — the canonical home of the contract.
/// </summary>
internal static class SchemaEndpoints
{
    private const string JsonSchemaMediaType = "application/schema+json";

    /// <summary>Contract file name keyed by the route segment.</summary>
    private static readonly Dictionary<string, string> Contracts = new(
        StringComparer.Ordinal)
    {
        ["clinical-schema"] = "clinical-schema.schema.json",
        ["ui-schema"] = "ui-schema.schema.json",
        ["rules-schema"] = "rules-schema.schema.json",
        ["workflow-schema"] = "workflow-schema.schema.json",
    };

    public static IEndpointRouteBuilder MapSchemaEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        _ = endpoints.MapGet(
                "/schemas/v1/{contract}.schema.json",
                (string contract, SchemaFilePaths paths, HttpContext http) =>
                    ServeContract(contract, paths, http))
            .AllowAnonymous()
            .WithName("GetSchemaContract")
            .WithTags("Schemas")
            .WithSummary("Serve a versioned clinical schema contract document")
            .Produces(StatusCodes.Status200OK, contentType: JsonSchemaMediaType)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>
    /// Serves one contract document. A document is immutable within its major
    /// version directory, so cache headers let CDNs and browsers reuse the
    /// bytes and the ETag supports revalidation across patch deploys.
    /// </summary>
    private static IResult ServeContract(
        string contract,
        SchemaFilePaths paths,
        HttpContext http)
    {
        if (!Contracts.TryGetValue(contract, out string? fileName))
        {
            return Results.NotFound(new
            {
                errors = new[]
                {
                    new
                    {
                        status = "404",
                        title = "Unknown schema contract",
                        detail = $"No schema contract named '{contract}' is served.",
                    },
                },
            });
        }

        string filePath = ResolveContractPath(paths, contract);
        if (!File.Exists(filePath))
        {
            return Results.NotFound(new
            {
                errors = new[]
                {
                    new
                    {
                        status = "404",
                        title = "Schema contract unavailable",
                        detail = $"Contract file '{fileName}' was not found.",
                    },
                },
            });
        }

        string json = File.ReadAllText(filePath);
        http.Response.Headers.ETag = ComputeEtag(json);
        http.Response.Headers.CacheControl = "public, max-age=300";
        return Results.Content(
            json,
            contentType: JsonSchemaMediaType);
    }

    private static string ResolveContractPath(
        SchemaFilePaths paths,
        string contract)
    {
        return contract switch
        {
            "clinical-schema" => paths.ClinicalSchemaPath,
            "ui-schema" => paths.UiSchemaPath,
            "rules-schema" => paths.RulesSchemaPath,
            "workflow-schema" => paths.WorkflowSchemaPath,
            _ => throw new ArgumentOutOfRangeException(
                nameof(contract),
                contract,
                "Unreachable: contract is validated before resolution."),
        };
    }

    private static string ComputeEtag(string json)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(json));
        return $"\"{Convert.ToHexString(hash)}\"";
    }
}
