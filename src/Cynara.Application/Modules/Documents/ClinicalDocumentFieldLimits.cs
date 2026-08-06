namespace Cynara.Application.Modules.Documents;

/// <summary>
/// Shared length budgets for clinical document string fields. Kept in the
/// application layer so workflow helpers share the same constants without
/// coupling to Infrastructure.
/// </summary>
public static class ClinicalDocumentFieldLimits
{
    /// <summary>Maximum length for the document author identifier.</summary>
    public const int AuthorIdMaxLength = 128;
}
