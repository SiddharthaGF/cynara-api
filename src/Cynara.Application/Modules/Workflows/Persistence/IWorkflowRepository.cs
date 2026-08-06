using Cynara.Domain.Workflows;

namespace Cynara.Application.Modules.Workflows.Persistence;

public interface IWorkflowRepository
{
    public Task<bool> CodeExistsAsync(
        string code,
        Guid hospitalId,
        CancellationToken cancellationToken);

    public void AddDefinition(WorkflowDefinition definition, WorkflowVersion draft);

    public Task<IReadOnlyList<WorkflowDefinition>> ListDefinitionsAsync(
        Guid hospitalId,
        CancellationToken cancellationToken);

    public Task<WorkflowDefinition?> FindDefinitionByCodeAsync(
        string code,
        Guid hospitalId,
        bool track,
        CancellationToken cancellationToken);

    public Task<WorkflowVersion?> FindPublishedVersionAsync(
        string code,
        Guid hospitalId,
        string version,
        CancellationToken cancellationToken);

    public void AddVersion(WorkflowVersion version);

    public void RemoveVersion(WorkflowVersion version);
}
