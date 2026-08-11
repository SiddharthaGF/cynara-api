using Cynara.Application.Common;
using Cynara.Application.Modules.Forms.Persistence;
using Cynara.Domain.Forms;

namespace Cynara.Application.Forms;

internal static class FormWorkflowHelpers
{
    public static async Task<FormDefinition> RequireDefinitionAsync(
        IFormRepository forms,
        string code,
        bool track,
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        FormDefinition? definition = await forms.FindDefinitionByCodeAsync(
            code,
            hospitalId,
            track,
            cancellationToken).ConfigureAwait(false);
        return definition ?? throw new NotFoundException(
            $"Form '{code}' was not found.");
    }

    public static FormVersion RequireEditableVersion(FormDefinition definition)
    {
        return definition.Versions.SingleOrDefault(
                item => item.Status is FormVersionStatus.Draft
                    or FormVersionStatus.Review)
            ?? throw new NotFoundException(
                $"Form '{definition.Code}' has no editable version.");
    }

    public static FormVersion RequireDraft(FormDefinition definition)
    {
        return definition.Versions.SingleOrDefault(
                item => item.Status == FormVersionStatus.Draft)
            ?? throw new NotFoundException(
                $"Form '{definition.Code}' has no draft version.");
    }

    public static FormVersion RequireReviewVersion(FormDefinition definition)
    {
        return definition.Versions.SingleOrDefault(
                item => item.Status == FormVersionStatus.Review)
            ?? throw new NotFoundException(
                $"Form '{definition.Code}' has no version in review.");
    }

    public static void EnsureDraftConcurrency(
        FormVersion version,
        uint expectedRowVersion)
    {
        ConcurrencyGuard.Ensure(
            version.RowVersion,
            expectedRowVersion,
            "form draft");
    }
}
