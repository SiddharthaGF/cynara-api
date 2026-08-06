using Cynara.Application.Audit;
using Cynara.Application.Common;
using Cynara.Application.Modules.Documents.Persistence;
using Cynara.Application.Modules.Encounters;
using Cynara.Application.Modules.Encounters.Persistence;
using Cynara.Application.Modules.FormResponses;
using Cynara.Application.Modules.FormResponses.Persistence;
using Cynara.Application.Modules.Forms.Persistence;
using Cynara.Application.Modules.Hospitals;
using Cynara.Application.Persistence;
using Cynara.Domain.Documents;
using Cynara.Domain.Encounters;
using Cynara.Domain.Forms;

namespace Cynara.Application.Modules.Documents;

/// <summary>
/// Default implementation of <see cref="IClinicalDocumentService"/>. The
/// start workflow stamps ownership from the resolved hospital context,
/// rejects retired catalog entries and non-open encounters, resolves the
/// exact published form snapshot, creates the bound form response, and
/// enforces the catalog multiplicity policy per encounter before emitting
/// audit events that commit in the same unit-of-work transaction.
/// </summary>
public sealed class ClinicalDocumentService(
    IClinicalDocumentRepository documents,
    IDocumentCatalogRepository catalog,
    IEncounterRepository encounters,
    IFormRepository forms,
    IFormResponseRepository responses,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IHospitalContext hospitalContext,
    TimeProvider timeProvider) : IClinicalDocumentService
{
    public async Task<ClinicalDocumentDto> StartAsync(
        StartClinicalDocumentRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        Guid hospitalId = hospitalContext.HospitalId;

        ClinicalDocumentWorkflowHelpers.EnsureValidAuthorId(actorId);
        DocumentDefinition definition = await RequireActiveDefinitionAsync(
                request.DocumentDefinitionId, cancellationToken)
            .ConfigureAwait(false);
        Encounter encounter = await RequireOpenEncounterAsync(
                request.EncounterId, cancellationToken)
            .ConfigureAwait(false);
        FormVersion formVersion = await RequirePublishedFormVersionAsync(
                definition.FormVersionId, cancellationToken)
            .ConfigureAwait(false);

        if (definition.RequiresActorForCreation
            && string.IsNullOrWhiteSpace(actorId))
        {
            throw new ValidationException(
                $"Catalog entry '{definition.Code}' requires an "
                + "authenticated actor to start documents.");
        }

        if (!definition.AllowsMultipleInstancesPerEncounter
            && await documents.AnyInstanceExistsAsync(
                hospitalId,
                definition.Id,
                encounter.Id,
                cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException(
                $"Catalog entry '{definition.Code}' allows a single document "
                + "per encounter, and one already exists for encounter "
                + $"'{encounter.Id}'.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        var response = new FormResponse
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalId,
            FormVersionId = formVersion.Id,
            Status = FormResponseStatus.Draft,
            AnswersJson = "{}",
            RevisionNumber = 1,
            RowVersion = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };
        FormResponseRevision revision = FormResponseWorkflowHelpers
            .CreateRevision(response, actorId, now);

        var document = new ClinicalDocument
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalId,
            DocumentDefinitionId = definition.Id,
            PatientId = encounter.PatientId,
            EncounterId = encounter.Id,
            FormVersionId = formVersion.Id,
            FormResponseId = response.Id,
            AuthorId = actorId,
            Status = ClinicalDocumentStatus.InProgress,
            CreatedAt = now,
            UpdatedAt = now,
        };

        auditWriter.Append(
            AuditEntityTypes.ClinicalDocument,
            document.Id,
            "document.started",
            actorId,
            now,
            new
            {
                documentDefinitionId = document.DocumentDefinitionId,
                encounterId = document.EncounterId,
                patientId = document.PatientId,
                formVersionId = document.FormVersionId,
                formResponseId = document.FormResponseId,
            });

        responses.Add(response, revision);
        documents.Add(document);
        _ = await unitOfWork.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        return ClinicalDocumentMappers.ToDto(document);
    }

    /// <inheritdoc />
    public async Task<ClinicalDocumentDto> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        ClinicalDocument document = await documents
            .FindByIdAsync(
                hospitalContext.HospitalId, id, track: false, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Clinical document '{id}' was not found.");
        return ClinicalDocumentMappers.ToDto(document);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClinicalDocumentDto>> ListAsync(
        ClinicalDocumentListRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();

        ClinicalDocumentListCriteria criteria = new(
            request.EncounterId,
            request.PatientId,
            request.DocumentDefinitionId,
            ClinicalDocumentWorkflowHelpers.ParseStatusOrNull(request.Status));

        IReadOnlyList<ClinicalDocument> matches = await documents
            .ListAsync(hospitalContext.HospitalId, criteria, cancellationToken)
            .ConfigureAwait(false);
        return [.. matches.Select(ClinicalDocumentMappers.ToDto)];
    }

    private async Task<DocumentDefinition> RequireActiveDefinitionAsync(
        Guid documentDefinitionId,
        CancellationToken cancellationToken)
    {
        DocumentDefinition definition = await catalog
            .FindByIdAsync(
                hospitalContext.HospitalId,
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

    private async Task<Encounter> RequireOpenEncounterAsync(
        Guid encounterId,
        CancellationToken cancellationToken)
    {
        Encounter encounter = await encounters
            .FindByIdAsync(
                hospitalContext.HospitalId,
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

    private async Task<FormVersion> RequirePublishedFormVersionAsync(
        Guid formVersionId,
        CancellationToken cancellationToken)
    {
        List<FormDefinition> definitions = [.. await forms
            .ListDefinitionsAsync(hospitalContext.HospitalId, cancellationToken)
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
}
