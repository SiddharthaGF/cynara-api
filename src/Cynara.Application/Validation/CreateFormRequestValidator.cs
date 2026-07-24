using Cynara.Application.Forms;

using FluentValidation;

namespace Cynara.Application.Validation;

public sealed class CreateFormRequestValidator : AbstractValidator<CreateFormRequest>
{
    public CreateFormRequestValidator()
    {
        _ = RuleFor(request => request.Code)
            .NotEmpty()
            .MaximumLength(128)
            .Matches("^[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*$")
            .WithMessage(
                "Form code must match pattern ^[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*$ and be at most 128 characters.");
        _ = RuleFor(request => request.Name)
            .NotEmpty()
            .MaximumLength(256);
        _ = RuleFor(request => request.ClinicalSchemaJson)
            .NotEmpty();
    }
}
