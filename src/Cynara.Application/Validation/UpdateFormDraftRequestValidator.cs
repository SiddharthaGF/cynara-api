using Cynara.Application.Forms;

using FluentValidation;

namespace Cynara.Application.Validation;

public sealed class UpdateFormDraftRequestValidator
    : AbstractValidator<UpdateFormDraftRequest>
{
    public UpdateFormDraftRequestValidator()
    {
        _ = RuleFor(request => request.ClinicalSchemaJson).NotEmpty();
    }
}
