using Cynara.Domain.Documents;

namespace Cynara.Application.Modules.Documents;

/// <summary>
/// Shared validation helpers for the document catalog workflows: stable
/// codes, optimistic concurrency, duplicate-code rejection, and prohibition
/// of edits against retired catalog entries.
/// </summary>
internal static class DocumentCatalogWorkflowHelpers
{
    public static void EnsureValidCode(string code, string entityName)
    {
        Domain.Common.ResourceCodeRules.EnsureValid(code, entityName);
    }

    public static void EnsureConcurrency(uint current, uint provided, string entityName)
    {
        ConcurrencyGuard.Ensure(current, provided, entityName);
    }

    public static void EnsureNotRetired(
        DocumentDefinitionStatus status,
        string entityName,
        string code)
    {
        StatusGuard.EnsureNotRetired(
            status,
            DocumentDefinitionStatus.Retired,
            entityName,
            code);
    }
}
