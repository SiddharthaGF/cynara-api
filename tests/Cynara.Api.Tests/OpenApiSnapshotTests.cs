using System.Text;

using Cynara.Api.JsonApi.OpenApi;

using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace Cynara.Api.Tests;

/// <summary>
/// Drift, determinism, and structural-validation tests for the committed
/// OpenAPI contract. Generation runs fully in-process through
/// <see cref="OpenApiDocumentExporter"/>, so no Postgres fixture is needed.
/// </summary>
public sealed class OpenApiSnapshotTests
{
    private const string ContractPath = "contracts/openapi.json";

    [Fact]
    public async Task Export_MatchesCommittedContract()
    {
        string exported = await OpenApiDocumentExporter.ExportAsync()
            .ConfigureAwait(false);
        string committedPath = Path.Combine(FindRepositoryRoot(), ContractPath);
        string committed = await File.ReadAllTextAsync(committedPath)
            .ConfigureAwait(false);

        Assert.True(
            string.Equals(
                NormalizeNewLines(exported),
                NormalizeNewLines(committed),
                StringComparison.Ordinal),
            "contracts/openapi.json drifted from the exporter output. Run "
            + "`dotnet cake --target=OpenApiExport` and commit the regenerated "
            + "file together with the endpoint or schema change.");
    }

    [Fact]
    public async Task Export_IsDeterministic()
    {
        string first = await OpenApiDocumentExporter.ExportAsync()
            .ConfigureAwait(false);
        string second = await OpenApiDocumentExporter.ExportAsync()
            .ConfigureAwait(false);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Export_LoadsUnderOpenApi30ValidationRules()
    {
        string json = await OpenApiDocumentExporter.ExportAsync()
            .ConfigureAwait(false);
        var settings = new OpenApiReaderSettings
        {
            RuleSet = ValidationRuleSet.GetDefaultRuleSet(),
        };

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        ReadResult result = await OpenApiDocument.LoadAsync(stream, "json", settings)
            .ConfigureAwait(false);

        Assert.NotNull(result.Document);
        OpenApiDiagnostic diagnostic = result.Diagnostic
            ?? throw new InvalidOperationException(
                "OpenAPI parse produced no diagnostic.");
        Assert.True(
            diagnostic.Errors.Count == 0,
            string.Join(
                Environment.NewLine,
                diagnostic.Errors.Select(static error => error.ToString())));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Cynara.Api.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate repository root (Cynara.Api.sln) from "
            + AppContext.BaseDirectory);
    }

    private static string NormalizeNewLines(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
