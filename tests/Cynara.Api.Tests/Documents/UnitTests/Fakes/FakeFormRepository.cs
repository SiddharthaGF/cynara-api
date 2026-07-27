using Cynara.Application.Modules.Forms.Persistence;
using Cynara.Domain.Forms;

namespace Cynara.Api.Tests.Documents.UnitTests.Fakes;

/// <summary>
/// In-memory fake of <see cref="IFormRepository"/> limited to the surface
/// the document catalog workflow actually calls. The fake is intentionally
/// minimal so we only depend on the contract under test.
/// </summary>
public sealed class FakeFormRepository : IFormRepository
{
    private readonly List<FormDefinition> definitions = [];

    public IReadOnlyCollection<FormDefinition> Definitions => definitions;

    public void Seed(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definitions.Add(definition);
    }

    public Task<bool> CodeExistsAsync(
        string code,
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        bool exists = definitions.Exists(
            item => item.HospitalId == hospitalId
                && string.Equals(item.Code, code, StringComparison.Ordinal));
        return Task.FromResult(exists);
    }

    public void AddDefinition(FormDefinition definition, FormVersion draft)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(draft);
        definitions.Add(definition);
    }

    public Task<IReadOnlyList<FormDefinition>> ListDefinitionsAsync(
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        var matches = definitions
            .Where(item => item.HospitalId == hospitalId)
            .ToList();
        return Task.FromResult<IReadOnlyList<FormDefinition>>(matches);
    }

    public Task<FormDefinition?> FindDefinitionByCodeAsync(
        string code,
        Guid hospitalId,
        bool track,
        CancellationToken cancellationToken)
    {
        FormDefinition? match = definitions.SingleOrDefault(
            item => item.HospitalId == hospitalId
                && string.Equals(item.Code, code, StringComparison.Ordinal));
        return Task.FromResult(match);
    }

    public void AddVersion(FormVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
    }

    public void RemoveVersion(FormVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
    }
}
