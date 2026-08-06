using Cynara.Application;
using Cynara.Application.Modules.Documents;
using Cynara.Application.Modules.Encounters;
using Cynara.Domain.Documents;
using Cynara.Domain.Encounters;
using Cynara.Domain.Forms;

namespace Cynara.Api.Tests.Documents.UnitTests.Fakes;

/// <summary>
/// In-memory fake of <see cref="IClinicalDocumentReferenceResolver"/> for
/// unit tests that exercise the document start workflow without the EF
/// Core stack. It composes the catalog, encounter, and form fakes and
/// mirrors the reference rules the real resolver enforces: active catalog
/// entries, open encounters, and published form snapshots.
/// </summary>
public sealed class FakeClinicalDocumentReferenceResolver(
    FakeDocumentCatalogRepository catalog,
    FakeEncounterRepository encounters,
    FakeFormRepository forms) : IClinicalDocumentReferenceResolver
{
    public Task<DocumentDefinition> RequireActiveDefinitionAsync(
        Guid hospitalId,
        Guid documentDefinitionId,
        CancellationToken cancellationToken)
    {
        DocumentDefinition definition = catalog.Entries.SingleOrDefault(
            item => item.Id == documentDefinitionId
                && item.HospitalId == hospitalId)
            ?? throw new NotFoundException(
                $"Document definition '{documentDefinitionId}' was not found.");

        if (definition.Status == DocumentDefinitionStatus.Retired)
        {
            throw new InvalidStateException(
                $"Document definition '{definition.Code}' is retired; new "
                + "documents cannot be started from a retired catalog entry.");
        }

        return Task.FromResult(definition);
    }

    public Task<Encounter> RequireOpenEncounterAsync(
        Guid hospitalId,
        Guid encounterId,
        CancellationToken cancellationToken)
    {
        Encounter encounter = encounters.Entries.SingleOrDefault(
            item => item.Id == encounterId && item.HospitalId == hospitalId)
            ?? throw new NotFoundException(
                $"Encounter '{encounterId}' was not found.");

        if (encounter.Status != EncounterStatus.Open)
        {
            throw new InvalidStateException(
                $"Encounter '{encounterId}' is "
                + EncounterWorkflowHelpers.FormatStatus(encounter.Status)
                + "; documents can only be started for open encounters.");
        }

        return Task.FromResult(encounter);
    }

    public Task<FormVersion> RequirePublishedFormVersionAsync(
        Guid hospitalId,
        Guid formVersionId,
        CancellationToken cancellationToken)
    {
        FormVersion? formVersion = forms.Definitions
            .Where(definition => definition.HospitalId == hospitalId)
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

        return Task.FromResult(formVersion);
    }
}
