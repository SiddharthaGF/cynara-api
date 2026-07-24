using Cynara.Application.Forms;
using Cynara.Application.Validation;

namespace Cynara.Api.Tests;

public sealed class SnapshotContractTests
{
    [Fact]
    public async Task FormVersionLifecycle_PermittedTransitions()
    {
        IReadOnlyList<string> transitions =
            FormVersionLifecycle.DescribePermittedTransitions();
        await Verify(transitions);
    }

    [Fact]
    public async Task CreateFormRequestValidator_InvalidCode_Errors()
    {
        var validator = new CreateFormRequestValidator();
        var request = new CreateFormRequest(
            "BAD CODE",
            string.Empty,
            string.Empty,
            UiSchemaJson: null);
        FluentValidation.Results.ValidationResult result =
            await validator.ValidateAsync(request).ConfigureAwait(false);
        await Verify(
            result.Errors
                .OrderBy(static error => error.PropertyName, StringComparer.Ordinal)
                .ThenBy(static error => error.ErrorMessage, StringComparer.Ordinal)
                .Select(static error => $"{error.PropertyName}: {error.ErrorMessage}")
                .ToArray());
    }
}
