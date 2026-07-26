using Cynara.Domain.ClinicalTaxonomy;

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
        if (string.IsNullOrWhiteSpace(code)
            || code.Length < Facility.Codes.MinLength
            || code.Length > Facility.Codes.MaxLength)
        {
            throw new ValidationException(
                $"{entityName} code '{code}' must be "
                + $"{Facility.Codes.MinLength}-{Facility.Codes.MaxLength} characters.");
        }
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
        ClinicalTaxonomyStatus status,
        string entityName,
        string code)
    {
        if (status == ClinicalTaxonomyStatus.Retired)
        {
            throw new InvalidStateException(
                $"{entityName} '{code}' is already retired.");
        }
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
