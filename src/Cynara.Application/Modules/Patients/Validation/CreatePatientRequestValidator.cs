using FluentValidation;

namespace Cynara.Application.Modules.Patients.Validation;

/// <summary>
/// FluentValidation rules for <see cref="CreatePatientRequest"/>. Domain
/// semantics (MRN uniqueness, demographic checks) are enforced by the
/// <see cref="PatientWorkflowHelpers"/> inside the service workflow; this
/// validator only catches structural issues at the request boundary so
/// the controller can return a 400 with structured errors before any
/// database round-trip occurs.
/// </summary>
public sealed class CreatePatientRequestValidator
    : AbstractValidator<CreatePatientRequest>
{
    private const string MaxLengthMessageSuffix =
        " characters or fewer.";

    private const string MrnMaxLengthMessage =
        "Patient MRN must be " + nameof(PatientFieldLimits.MrnMaxLength)
        + MaxLengthMessageSuffix;

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

    public CreatePatientRequestValidator()
    {
        _ = RuleFor(item => item.Mrn)
            .NotEmpty()
                .WithMessage("Patient MRN is required.")
            .MaximumLength(PatientWorkflowHelpers.MrnMaxLength)
                .WithMessage(MrnMaxLengthMessage);

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
    }
}
