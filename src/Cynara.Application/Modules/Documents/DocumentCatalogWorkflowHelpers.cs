using Cynara.Domain.Documents;

namespace Cynara.Application.Modules.Documents;

/// <summary>
/// Shared validation helpers for the clinical document catalog workflows.
/// Keeps the lifecycle service free of repeated parameter sanitation while
/// preserving the rules described in CYN-36: stable codes, optimistic
/// concurrency, rejection of duplicate codes, and prohibition of edits
/// against retired catalog entries.
/// </summary>
internal static class DocumentCatalogWorkflowHelpers
{
    public static void EnsureValidCode(string code, string entityName)
    {
        Domain.Common.ResourceCodeRules.EnsureValid(code, entityName);
    }

    public static void EnsureConcurrency(uint current, uint provided, string entityName)
    {
        if (current != provided)
        {
            throw new ConcurrencyException(
                $"The {entityName} was modified by another request.");
        }
    }

    public static void EnsureNotRetired(
        DocumentDefinitionStatus status,
        string entityName,
        string code)
    {
        if (status == DocumentDefinitionStatus.Retired)
        {
            throw new InvalidStateException(
                $"{entityName} '{code}' is already retired.");
        }
    }
}
