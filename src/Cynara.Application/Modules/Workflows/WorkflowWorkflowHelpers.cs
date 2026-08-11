using Cynara.Application.Common;
using Cynara.Application.Modules.Workflows.Persistence;
using Cynara.Domain.Workflows;

namespace Cynara.Application.Modules.Workflows;

internal static class WorkflowWorkflowHelpers
{
    public static async Task<WorkflowDefinition> RequireDefinitionAsync(
        IWorkflowRepository workflows,
        string code,
        bool track,
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        WorkflowDefinition? definition = await workflows
            .FindDefinitionByCodeAsync(code, hospitalId, track, cancellationToken)
            .ConfigureAwait(false);
        return definition ?? throw new NotFoundException(
            $"Workflow '{code}' was not found.");
    }

    public static WorkflowVersion RequireEditableVersion(
        WorkflowDefinition definition)
    {
        return definition.Versions.SingleOrDefault(
                item => item.Status is WorkflowVersionStatus.Draft
                    or WorkflowVersionStatus.Review)
            ?? throw new NotFoundException(
                $"Workflow '{definition.Code}' has no editable version.");
    }

    public static WorkflowVersion RequireDraft(
        WorkflowDefinition definition)
    {
        return definition.Versions.SingleOrDefault(
                item => item.Status == WorkflowVersionStatus.Draft)
            ?? throw new NotFoundException(
                $"Workflow '{definition.Code}' has no draft version.");
    }

    public static WorkflowVersion RequireReviewVersion(
        WorkflowDefinition definition)
    {
        return definition.Versions.SingleOrDefault(
                item => item.Status == WorkflowVersionStatus.Review)
            ?? throw new NotFoundException(
                $"Workflow '{definition.Code}' has no version in review.");
    }

    public static void EnsureDraftConcurrency(
        WorkflowVersion version,
        uint expectedRowVersion)
    {
        ConcurrencyGuard.Ensure(
            version.RowVersion,
            expectedRowVersion,
            "workflow draft");
    }
}
