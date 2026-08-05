using System.Globalization;

using FluentValidation;

namespace Cynara.Application.Modules.Encounters.Validation;

/// <summary>
/// FluentValidation rules for <see cref="CreateEncounterRequest"/>. Domain
/// semantics (reference resolution, retired parents) are enforced by the
/// service workflow; this validator catches structural issues at the
/// request boundary.
/// </summary>
public sealed class CreateEncounterRequestValidator
    : AbstractValidator<CreateEncounterRequest>
{
    private static readonly HashSet<string> AllowedTypes = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "ambulatory",
        "emergency",
        "inpatient",
        "observation",
        "virtual",
    };

    public CreateEncounterRequestValidator()
    {
        _ = RuleFor(item => item.PatientId)
            .NotEmpty()
            .WithMessage("Encounter patientId is required.");

        _ = RuleFor(item => item.FacilityId)
            .NotEmpty()
            .WithMessage("Encounter facilityId is required.");

        _ = RuleFor(item => item.ClinicalAreaId)
            .NotEmpty()
            .WithMessage("Encounter clinicalAreaId is required.");

        _ = RuleFor(item => item.Type)
            .NotEmpty()
            .WithMessage("Encounter type is required.")
            .Must(AllowedTypes.Contains)
            .WithMessage(item =>
                "Encounter type '" + item.Type
                + "' is not one of: ambulatory, emergency, inpatient, "
                + "observation, virtual.");

        _ = RuleFor(item => item.ResponsibleProfessionalId)
            .NotEmpty()
            .WithMessage(
                "Encounter responsible professional identifier is required.")
            .MaximumLength(EncounterFieldLimits.ResponsibleProfessionalIdMaxLength)
            .WithMessage(
                "Encounter responsible professional identifier must be "
                + EncounterFieldLimits.ResponsibleProfessionalIdMaxLength
                    .ToString(CultureInfo.InvariantCulture)
                + " characters or fewer.");
    }
}
