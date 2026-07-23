using Cynara.Application.Forms;

using FluentValidation;

namespace Cynara.Application.Validation;

public sealed class CreateFormResponseRequestValidator
    : AbstractValidator<CreateFormResponseRequest>
{
    public CreateFormResponseRequestValidator()
    {
        _ = RuleFor(request => request.AnswersJson)
            .Must(static value => value is null || !string.IsNullOrWhiteSpace(value))
            .WithMessage("Answers JSON must not be blank when provided.");
    }
}
