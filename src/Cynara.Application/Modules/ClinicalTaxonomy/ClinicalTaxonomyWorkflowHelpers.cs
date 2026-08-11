using Cynara.Application.Common;

using Cynara.Domain.ClinicalTaxonomy;
using Cynara.Domain.Common;

namespace Cynara.Application.Modules.ClinicalTaxonomy;

/// <summary>
/// Shared validation helpers for the clinical taxonomy workflows. Keeps
/// the lifecycle services free of repeated parameter sanitation while
/// preserving the rules described in CYN-35: stable codes, optimistic
/// concurrency, and rejection of new activity targeting retired parents.
/// </summary>
internal static class ClinicalTaxonomyWorkflowHelpers
{
    public static void EnsureValidCode(
        string code,
        string entityName)
    {
        ResourceCodeRules.EnsureValid(code, entityName);
    }

    public static void EnsureConcurrency(uint current, uint provided, string entityName)
    {
        ConcurrencyGuard.Ensure(current, provided, entityName);
    }

    public static void EnsureNotRetired(
        ClinicalTaxonomyStatus status,
        string entityName,
        string code)
    {
        StatusGuard.EnsureNotRetired(
            status,
            ClinicalTaxonomyStatus.Retired,
            entityName,
            code);
    }

    public static void EnsureParentActive(
        ClinicalTaxonomyStatus parentStatus,
        string parentName,
        string parentCode,
        string childName)
    {
        if (parentStatus == ClinicalTaxonomyStatus.Retired)
        {
            throw new InvalidStateException(
                $"{parentName} '{parentCode}' is retired; "
                + $"new {childName} cannot be created under a retired parent.");
        }
    }
}
