using System.Globalization;

using Cynara.Application.Common;

using Cynara.Domain.Encounters;

namespace Cynara.Application.Modules.Encounters;

/// <summary>
/// Shared validation and formatting helpers for the encounter lifecycle.
/// </summary>
internal static class EncounterWorkflowHelpers
{
    public static void EnsureConcurrency(uint current, uint provided)
    {
        ConcurrencyGuard.Ensure(current, provided, "encounter");
    }

    public static void EnsureValidResponsibleProfessionalId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(
                "Encounter responsible professional identifier is required.");
        }

        if (value.Trim().Length > EncounterFieldLimits.ResponsibleProfessionalIdMaxLength)
        {
            throw new ValidationException(
                "Encounter responsible professional identifier must be "
                + EncounterFieldLimits.ResponsibleProfessionalIdMaxLength
                    .ToString(CultureInfo.InvariantCulture)
                + " characters or fewer.");
        }
    }

    public static EncounterType ParseType(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        if (!Enum.TryParse(value, ignoreCase: true, out EncounterType parsed)
            || !Enum.IsDefined(parsed))
        {
            throw new ValidationException(
                "Encounter type '" + value
                + "' is not one of: ambulatory, emergency, inpatient, "
                + "observation, virtual.");
        }

        return parsed;
    }

    public static EncounterStatus? ParseStatusOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (string.Equals(value, "enteredInError", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "entered-in-error", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "entered_in_error", StringComparison.OrdinalIgnoreCase))
        {
            return EncounterStatus.EnteredInError;
        }

        if (!Enum.TryParse(value, ignoreCase: true, out EncounterStatus parsed)
            || !Enum.IsDefined(parsed))
        {
            throw new ValidationException(
                "Encounter status '" + value
                + "' is not one of: open, completed, canceled, enteredInError.");
        }

        return parsed;
    }

    public static string FormatType(EncounterType type)
    {
        return type.ToString().ToLowerInvariant();
    }

    public static string FormatStatus(EncounterStatus status)
    {
        return status switch
        {
            EncounterStatus.Open => "open",
            EncounterStatus.Completed => "completed",
            EncounterStatus.Canceled => "canceled",
            EncounterStatus.EnteredInError => "enteredInError",
            _ => status.ToString().ToLowerInvariant(),
        };
    }

    public static void EnsureEndedAtNotBeforeStart(
        DateTimeOffset startedAt,
        DateTimeOffset endedAt)
    {
        if (endedAt < startedAt)
        {
            throw new ValidationException(
                "Encounter endedAt cannot be earlier than startedAt.");
        }
    }
}
