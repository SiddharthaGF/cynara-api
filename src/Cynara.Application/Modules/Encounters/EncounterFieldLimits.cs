namespace Cynara.Application.Modules.Encounters;

/// <summary>
/// Shared length budgets for encounter string fields. Kept in the
/// application layer so validation and workflow helpers share the
/// same constants without coupling to Infrastructure.
/// </summary>
public static class EncounterFieldLimits
{
    /// <summary>Maximum length for the responsible professional identifier.</summary>
    public const int ResponsibleProfessionalIdMaxLength = 128;
}
