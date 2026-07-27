using FluentValidation;

namespace Cynara.Application.Modules.Patients.Validation;

/// <summary>
/// FluentValidation rules for <see cref="UpdatePatientRequest"/>. Domain
/// semantics (concurrency, soft-delete guard) are enforced inside the
/// service workflow; this validator only catches structural issues at the
/// request boundary.
/// </summary>
public sealed class UpdatePatientRequestValidator
    : AbstractValidator<UpdatePatientRequest>
{
    private const string MaxLengthMessageSuffix =
        " characters or fewer.";

    private const string NationalIdMaxLengthMessage =
        "Patient national identifier must be "
        + nameof(PatientFieldLimits.NationalIdMaxLength)
        + MaxLengthMessageSuffix;

    private const string GivenNameMaxLengthMessage =
        "Patient given name must be "
        + nameof(PatientFieldLimits.NameMaxLength)
        + MaxLengthMessageSuffix;

    private const string FamilyNameMaxLengthMessage =
        "Patient family name must be "
        + nameof(PatientFieldLimits.NameMaxLength)
        + MaxLengthMessageSuffix;

    public UpdatePatientRequestValidator()
    {
        _ = RuleFor(item => item.NationalId)
            .MaximumLength(PatientWorkflowHelpers.NationalIdMaxLength)
                .WithMessage(NationalIdMaxLengthMessage)
                .When(item => !string.IsNullOrWhiteSpace(item.NationalId));

        _ = RuleFor(item => item.GivenName)
            .NotEmpty()
                .WithMessage("Patient given name is required.")
            .MaximumLength(PatientWorkflowHelpers.NameMaxLength)
                .WithMessage(GivenNameMaxLengthMessage);

        _ = RuleFor(item => item.FamilyName)
            .NotEmpty()
                .WithMessage("Patient family name is required.")
            .MaximumLength(PatientFieldLimits.NameMaxLength)
                .WithMessage(FamilyNameMaxLengthMessage);

        _ = RuleFor(item => item.Sex)
            .NotEmpty()
                .WithMessage("Patient sex is required.")
            .Must(value => value is "female" or "male" or "unknown")
                .WithMessage(item =>
                    "Patient sex '" + item.Sex
                    + "' is not one of: female, male, unknown.");

        _ = RuleFor(item => item.RowVersion)
            .GreaterThanOrEqualTo(0U)
                .WithMessage("Patient rowVersion must be a non-negative integer.");
    }
}
