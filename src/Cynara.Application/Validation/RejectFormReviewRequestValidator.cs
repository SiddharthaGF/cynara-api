using Cynara.Application.Forms;

using FluentValidation;

namespace Cynara.Application.Validation;

public sealed class RejectFormReviewRequestValidator
    : AbstractValidator<RejectFormReviewRequest>
{
    public RejectFormReviewRequestValidator()
    {
        _ = RuleFor(request => request.Comment)
            .NotEmpty()
            .WithMessage("Review rejection comment is required.");
    }
}
