using Cynara.Application.Modules.Documents.Persistence;
using Cynara.Application.Modules.Encounters;
using Cynara.Application.Modules.Encounters.Persistence;
using Cynara.Application.Modules.Forms.Persistence;
using Cynara.Domain.Documents;
using Cynara.Domain.Encounters;
using Cynara.Domain.Forms;

namespace Cynara.Application.Modules.Documents;

/// <summary>
/// Default implementation of <see cref="IClinicalDocumentReferenceResolver"/>.
/// Loads reference data through the catalog, encounter, and form repository
/// ports and applies the state rules that gate document creation: active
/// catalog entries, open encounters, and published form snapshots.
/// </summary>
public sealed class ClinicalDocumentReferenceResolver(
    IDocumentCatalogRepository catalog,
    IEncounterRepository encounters,
    IFormRepository forms) : IClinicalDocumentReferenceResolver
{
    /// <inheritdoc />
    public async Task<DocumentDefinition> RequireActiveDefinitionAsync(
        Guid hospitalId,
        Guid documentDefinitionId,
        CancellationToken cancellationToken)
    {
        DocumentDefinition definition = await catalog
            .FindByIdAsync(
                hospitalId,
                documentDefinitionId,
                track: false,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Document definition '{documentDefinitionId}' was not found.");

        if (definition.Status == DocumentDefinitionStatus.Retired)
        {
            throw new InvalidStateException(
                $"Document definition '{definition.Code}' is retired; new "
                + "documents cannot be started from a retired catalog entry.");
        }

        return definition;
    }

    /// <inheritdoc />
    public async Task<Encounter> RequireOpenEncounterAsync(
        Guid hospitalId,
        Guid encounterId,
        CancellationToken cancellationToken)
    {
        Encounter encounter = await encounters
            .FindByIdAsync(
                hospitalId,
                encounterId,
                track: false,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Encounter '{encounterId}' was not found.");

        if (encounter.Status != EncounterStatus.Open)
        {
            throw new InvalidStateException(
                $"Encounter '{encounterId}' is "
                + EncounterWorkflowHelpers.FormatStatus(encounter.Status)
                + "; documents can only be started for open encounters.");
        }

        return encounter;
    }

    /// <inheritdoc />
    public async Task<FormVersion> RequirePublishedFormVersionAsync(
        Guid hospitalId,
        Guid formVersionId,
        CancellationToken cancellationToken)
    {
        List<FormDefinition> definitions = [.. await forms
            .ListDefinitionsAsync(hospitalId, cancellationToken)
            .ConfigureAwait(false)];

        FormVersion? formVersion = definitions
            .SelectMany(definition => definition.Versions)
            .FirstOrDefault(item => item.Id == formVersionId)
            ?? throw new NotFoundException(
                $"Form version '{formVersionId}' was not found.");

        if (formVersion.Status != FormVersionStatus.Published)
        {
            throw new ConflictException(
                $"Form version '{formVersionId}' is not published and cannot "
                + "accept new document instances.");
        }

        return formVersion;
    }

    /// <inheritdoc />
    public async Task<DocumentDefinition> RequireDefinitionAsync(
        Guid hospitalId,
        Guid documentDefinitionId,
        CancellationToken cancellationToken)
    {
        return await catalog
            .FindByIdAsync(
                hospitalId,
                documentDefinitionId,
                track: false,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Document definition '{documentDefinitionId}' was not found.");
    }
}
