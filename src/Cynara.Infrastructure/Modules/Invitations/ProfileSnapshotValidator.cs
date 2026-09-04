using System.Text.Json;

using Cynara.Application.Modules.Invitations;
using Cynara.Infrastructure.Schemas;

using Json.Schema;

namespace Cynara.Infrastructure.Modules.Invitations;

/// <summary>
/// JsonSchema.Net-backed validator for the canonical invitation profile
/// snapshot contract. Invalid JSON collapses to a single closed error;
/// schema violations are flattened into the same error-list shape so
/// callers treat malformed and non-conforming snapshots identically.
/// </summary>
public sealed class ProfileSnapshotValidator(SchemaFilePaths schemaPaths)
    : IProfileSnapshotValidator
{
    private readonly JsonSchema schema = JsonSchema.FromFile(
        schemaPaths.ProfileSnapshotPath);

    public Task<IReadOnlyList<string>> ValidateAsync(
        string snapshotJson,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var document = JsonDocument.Parse(snapshotJson);
            EvaluationResults results = schema.Evaluate(
                document.RootElement,
                new EvaluationOptions { OutputFormat = OutputFormat.List });
            if (results.IsValid)
            {
                return Task.FromResult<IReadOnlyList<string>>([]);
            }

            List<string> errors = [];
            CollectErrors(results, errors);
            return Task.FromResult<IReadOnlyList<string>>(errors);
        }
        catch (JsonException)
        {
            return Task.FromResult<IReadOnlyList<string>>(
                ["snapshot is not valid JSON"]);
        }
    }

    private static void CollectErrors(
        EvaluationResults results,
        List<string> errors)
    {
        if (results.Errors is not null)
        {
            foreach (KeyValuePair<string, string> error in results.Errors)
            {
                errors.Add($"{error.Key}: {error.Value}");
            }
        }

        if (results.Details is null)
        {
            return;
        }

        foreach (EvaluationResults detail in results.Details)
        {
            CollectErrors(detail, errors);
        }
    }
}
