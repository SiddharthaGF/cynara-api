using System.Globalization;

using Cynara.Domain.Documents;

namespace Cynara.Application.Modules.Documents;

/// <summary>
/// Shared validation and formatting helpers for the clinical document
/// instance lifecycle.
/// </summary>
internal static class ClinicalDocumentWorkflowHelpers
{
    public static void EnsureValidAuthorId(string? actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return;
        }

        if (actorId.Trim().Length > ClinicalDocumentFieldLimits.AuthorIdMaxLength)
        {
            throw new ValidationException(
                "Clinical document author identifier must be "
                + ClinicalDocumentFieldLimits.AuthorIdMaxLength
                    .ToString(CultureInfo.InvariantCulture)
                + " characters or fewer.");
        }
    }

    public static ClinicalDocumentStatus? ParseStatusOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Enum.TryParse(value, ignoreCase: true, out ClinicalDocumentStatus parsed)
            || !Enum.IsDefined(parsed))
        {
            throw new ValidationException(
                "Clinical document status '" + value
                + "' is not one of: inProgress, completed.");
        }

        return parsed;
    }

    public static string FormatStatus(ClinicalDocumentStatus status)
    {
        return status switch
        {
            ClinicalDocumentStatus.InProgress => "inProgress",
            ClinicalDocumentStatus.Completed => "completed",
            _ => status.ToString().ToLowerInvariant(),
        };
    }
}
