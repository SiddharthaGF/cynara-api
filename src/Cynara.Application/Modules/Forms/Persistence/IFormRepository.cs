using Cynara.Domain.Forms;

namespace Cynara.Application.Modules.Forms.Persistence;

public interface IFormRepository
{
    public Task<bool> CodeExistsAsync(
        string code,
        Guid hospitalId,
        CancellationToken cancellationToken);

    public void AddDefinition(FormDefinition definition, FormVersion draft);

    public Task<IReadOnlyList<FormDefinition>> ListDefinitionsAsync(
        Guid hospitalId,
        CancellationToken cancellationToken);

    public Task<FormDefinition?> FindDefinitionByCodeAsync(
        string code,
        Guid hospitalId,
        bool track,
        CancellationToken cancellationToken);

    public Task<FormVersion?> FindVersionByIdAsync(
        Guid hospitalId,
        Guid formVersionId,
        CancellationToken cancellationToken);

    public void AddVersion(FormVersion version);

    public void RemoveVersion(FormVersion version);
}
