using Cynara.Domain.Forms;

namespace Cynara.Application.Modules.Forms.Persistence;

public interface IFormRepository
{
    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    public void AddDefinition(FormDefinition definition, FormVersion draft);

    public Task<IReadOnlyList<FormDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken);

    public Task<FormDefinition?> FindDefinitionByCodeAsync(string code, bool track, CancellationToken cancellationToken);

    public void AddVersion(FormVersion version);

    public void RemoveVersion(FormVersion version);
}
