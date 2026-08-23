using Cynara.Domain.Documents;
using Cynara.Domain.Encounters;
using Cynara.Domain.Forms;

namespace Cynara.Application.Modules.Documents;

/// <summary>
/// Resolves the reference data a clinical document needs before start: the
/// catalog entry, target encounter, and exact published form snapshot.
/// Grouping the lookups behind one port keeps
/// <see cref="IClinicalDocumentService"/> focused on instance invariants.
/// </summary>
public interface IClinicalDocumentReferenceResolver
{
    /// <summary>
    /// Returns the active catalog entry for
    /// <paramref name="documentDefinitionId"/> or throws when the entry is
    /// unknown or retired.
    /// </summary>
    public Task<DocumentDefinition> RequireActiveDefinitionAsync(
        Guid hospitalId,
        Guid documentDefinitionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the open encounter for <paramref name="encounterId"/> or
    /// throws when the encounter is unknown or not open.
    /// </summary>
    public Task<Encounter> RequireOpenEncounterAsync(
        Guid hospitalId,
        Guid encounterId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the published form version for <paramref name="formVersionId"/>
    /// or throws when the version is unknown or not published.
    /// </summary>
    public Task<FormVersion> RequirePublishedFormVersionAsync(
        Guid hospitalId,
        Guid formVersionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the catalog entry for <paramref name="documentDefinitionId"/>
    /// or throws when unknown. Unlike
    /// <see cref="RequireActiveDefinitionAsync"/>, retired entries stay
    /// resolvable so transitions can honor completion policy post-retirement.
    /// </summary>
    public Task<DocumentDefinition> RequireDefinitionAsync(
        Guid hospitalId,
        Guid documentDefinitionId,
        CancellationToken cancellationToken);
}
