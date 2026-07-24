using Cynara.Application.Forms;

using FluentValidation;

namespace Cynara.Application.Validation;

public sealed class UpdateFormResponseRequestValidator
    : AbstractValidator<UpdateFormResponseRequest>
{
    public UpdateFormResponseRequestValidator()
    {
        _ = RuleFor(request => request.AnswersJson)
            .NotEmpty()
            .WithMessage("Answers JSON is required.");
    }
}
