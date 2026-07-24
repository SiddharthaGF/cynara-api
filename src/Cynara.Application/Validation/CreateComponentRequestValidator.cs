using Cynara.Application.Components;

using FluentValidation;

namespace Cynara.Application.Validation;

public sealed class CreateComponentRequestValidator
    : AbstractValidator<CreateComponentRequest>
{
    public CreateComponentRequestValidator()
    {
        _ = RuleFor(request => request.Code)
            .NotEmpty()
            .MaximumLength(128)
            .Matches("^[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*$")
            .WithMessage(
                "Component code must match pattern ^[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*$ and be at most 128 characters.");
        _ = RuleFor(request => request.Name)
            .NotEmpty()
            .MaximumLength(256);
        _ = RuleFor(request => request.ClinicalSchemaJson)
            .NotEmpty();
    }
}
