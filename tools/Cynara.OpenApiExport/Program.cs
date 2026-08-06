using System.Text;

using Cynara.Api.JsonApi.OpenApi;

namespace Cynara.OpenApiExport;

/// <summary>
/// CLI wrapper around <see cref="OpenApiDocumentExporter"/> used by the
/// <c>dotnet cake --target=OpenApiExport</c> build task to write the committed
/// <c>contracts/openapi.json</c> document.
/// </summary>
internal static class Program
{
    private const string DefaultOutput = "contracts/openapi.json";

    public static async Task<int> Main(string[] args)
    {
        try
        {
            string outputPath = ResolveOutputPath(args);
            string json = await OpenApiDocumentExporter.ExportAsync()
                .ConfigureAwait(false);

            string fullPath = Path.GetFullPath(outputPath);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                _ = Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(
                fullPath,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                .ConfigureAwait(false);

            Console.WriteLine($"OpenAPI document written to {fullPath}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Export failed: {exception.Message}");
            return 1;
        }
    }

    private static string ResolveOutputPath(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--output", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(args[i + 1]))
            {
                return args[i + 1];
            }
        }

        return DefaultOutput;
    }
}
