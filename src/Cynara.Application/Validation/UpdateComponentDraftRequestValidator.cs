using Cynara.Application.Components;

using FluentValidation;

namespace Cynara.Application.Validation;

public sealed class UpdateComponentDraftRequestValidator
    : AbstractValidator<UpdateComponentDraftRequest>
{
    public UpdateComponentDraftRequestValidator()
    {
        _ = RuleFor(request => request.ClinicalSchemaJson).NotEmpty();
    }
}
