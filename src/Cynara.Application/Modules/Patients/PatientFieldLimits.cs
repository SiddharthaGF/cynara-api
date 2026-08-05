namespace Cynara.Application.Modules.Patients;

/// <summary>
/// Public constants for patient registry field length bounds. Lives in
/// the Application layer so both workflows and EF entity configurations
/// can share the same authoritative values without reaching into the
/// internal <see cref="PatientWorkflowHelpers"/>.
/// </summary>
public static class PatientFieldLimits
{
    /// <summary>Maximum length for the displayed MRN.</summary>
    public const int MrnMaxLength = 64;

    /// <summary>Maximum length for the national identifier.</summary>
    public const int NationalIdMaxLength = 64;

    /// <summary>Maximum length for given and family names.</summary>
    public const int NameMaxLength = 128;
}
