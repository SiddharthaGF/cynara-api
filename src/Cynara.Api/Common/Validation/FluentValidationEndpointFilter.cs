using FluentValidation;
using FluentValidation.Results;

namespace Cynara.Api.Common.Validation;

internal sealed class FluentValidationEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        foreach (object? argument in context.Arguments)
        {
            if (argument is null)
            {
                continue;
            }

            Type validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (context.HttpContext.RequestServices.GetService(validatorType)
                is not IValidator validator)
            {
                continue;
            }

            ValidationResult result = await validator
                .ValidateAsync(
                    new ValidationContext<object>(argument),
                    context.HttpContext.RequestAborted)
                .ConfigureAwait(false);
            if (result.IsValid)
            {
                continue;
            }

            string message = string.Join(
                ' ',
                result.Errors.Select(static error => error.ErrorMessage));
            throw new Application.ValidationException(message);
        }

        return await next(context).ConfigureAwait(false);
    }
}
