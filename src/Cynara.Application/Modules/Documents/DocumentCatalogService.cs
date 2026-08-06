using Cynara.Application.Audit;
using Cynara.Application.Common;
using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.ClinicalTaxonomy.Persistence;
using Cynara.Application.Modules.Documents.Persistence;
using Cynara.Application.Modules.Forms.Persistence;
using Cynara.Application.Modules.Hospitals;
using Cynara.Application.Persistence;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Documents;
using Cynara.Domain.Forms;

namespace Cynara.Application.Modules.Documents;

/// <summary>
/// Default implementation of <see cref="IDocumentCatalogService"/>. All
/// write operations stamp ownership from the resolved hospital context,
/// validate the published form version and clinical taxonomy references
/// server-side, and emit audit events that commit in the same
/// unit-of-work transaction. The pinned <c>FormVersionId</c> snapshot
/// survives retirement so historical documents remain resolvable.
/// </summary>
public sealed class DocumentCatalogService(
    IDocumentCatalogRepository repository,
    IFormRepository forms,
    IClinicalTaxonomyRepository taxonomy,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IHospitalContext hospitalContext,
    TimeProvider timeProvider,
    ICapabilityGuard capabilityGuard) : IDocumentCatalogService
{
    public async Task<IReadOnlyList<DocumentDefinitionDto>> ListAsync(
        bool includeRetired,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogRead, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<DocumentDefinition> entries = await repository
            .ListAsync(hospitalContext.HospitalId, includeRetired, cancellationToken)
            .ConfigureAwait(false);
        return [.. entries.Select(DocumentCatalogMappers.ToDto)];
    }

    public async Task<DocumentDefinitionDto> CreateAsync(
        CreateDocumentDefinitionRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogWrite, cancellationToken)
            .ConfigureAwait(false);
        DocumentCatalogWorkflowHelpers.EnsureValidCode(request.Code, "Document definition");
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("Document definition name is required.");
        }

        FormVersion formVersion = await FindPublishedFormVersionAsync(
                request.FormVersionId, cancellationToken)
            .ConfigureAwait(false);

        Domain.ClinicalTaxonomy.Facility facility = await taxonomy
            .FindFacilityByIdAsync(
                hospitalContext.HospitalId,
                request.FacilityId,
                track: false,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Facility '{request.FacilityId}' was not found.");

        Domain.ClinicalTaxonomy.ClinicalArea clinicalArea = await taxonomy
            .FindClinicalAreaByIdAsync(
                hospitalContext.HospitalId,
                request.ClinicalAreaId,
                track: false,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Clinical area '{request.ClinicalAreaId}' was not found.");

        if (clinicalArea.FacilityId != facility.Id)
        {
            throw new ValidationException(
                $"Clinical area '{clinicalArea.Code}' does not belong to "
                + $"facility '{facility.Code}'.");
        }

        Domain.ClinicalTaxonomy.Discipline discipline = await taxonomy
            .FindDisciplineByIdAsync(
                hospitalContext.HospitalId,
                request.DisciplineId,
                track: false,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Discipline '{request.DisciplineId}' was not found.");

        if (discipline.ClinicalAreaId != clinicalArea.Id)
        {
            throw new ValidationException(
                $"Discipline '{discipline.Code}' does not belong to "
                + $"clinical area '{clinicalArea.Code}'.");
        }

        if (await repository.CodeExistsAsync(
                hospitalContext.HospitalId,
                request.Code,
                cancellationToken)
            .ConfigureAwait(false))
        {
            throw new ConflictException(
                $"Document definition '{request.Code}' already exists.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        DocumentDefinition documentDefinition = new()
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalContext.HospitalId,
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            Status = DocumentDefinitionStatus.Active,
            FormDefinitionId = formVersion.FormDefinitionId,
            FormVersionId = formVersion.Id,
            FacilityId = facility.Id,
            ClinicalAreaId = clinicalArea.Id,
            DisciplineId = discipline.Id,
            AllowsMultipleInstancesPerEncounter = request.AllowsMultipleInstancesPerEncounter,
            RequiresActorForCreation = request.RequiresActorForCreation,
            RequiresActorForCompletion = request.RequiresActorForCompletion,
            CreatedAt = now,
            UpdatedAt = now,
        };

        auditWriter.Append(
            AuditEntityTypes.DocumentDefinition,
            documentDefinition.Id,
            "document-definition.created",
            actorId,
            now,
            new
            {
                code = documentDefinition.Code,
                formVersionId = formVersion.Id,
                facilityId = facility.Id,
                clinicalAreaId = clinicalArea.Id,
                disciplineId = discipline.Id,
            });

        repository.Add(documentDefinition);
        _ = await unitOfWork.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        return DocumentCatalogMappers.ToDto(documentDefinition);
    }

    public async Task<DocumentDefinitionDto> UpdateAsync(
        Guid id,
        UpdateDocumentDefinitionRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogWrite, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("Document definition name is required.");
        }

        DocumentDefinition documentDefinition = await repository
            .FindByIdAsync(hospitalContext.HospitalId, id, track: true, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Document definition '{id}' was not found.");

        DocumentCatalogWorkflowHelpers.EnsureConcurrency(
            documentDefinition.RowVersion,
            request.RowVersion,
            "document definition");
        DocumentCatalogWorkflowHelpers.EnsureNotRetired(
            documentDefinition.Status,
            "Document definition",
            documentDefinition.Code);

        DateTimeOffset now = timeProvider.GetUtcNow();
        documentDefinition.Name = request.Name.Trim();
        documentDefinition.AllowsMultipleInstancesPerEncounter =
            request.AllowsMultipleInstancesPerEncounter;
        documentDefinition.RequiresActorForCreation = request.RequiresActorForCreation;
        documentDefinition.RequiresActorForCompletion = request.RequiresActorForCompletion;
        documentDefinition.UpdatedAt = now;
        documentDefinition.RowVersion = request.RowVersion + 1;

        auditWriter.Append(
            AuditEntityTypes.DocumentDefinition,
            documentDefinition.Id,
            "document-definition.updated",
            actorId,
            now,
            new
            {
                code = documentDefinition.Code,
                rowVersion = request.RowVersion,
            });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        return DocumentCatalogMappers.ToDto(documentDefinition);
    }

    public async Task<DocumentDefinitionDto> RetireAsync(
        Guid id,
        RetireDocumentDefinitionRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogWrite, cancellationToken)
            .ConfigureAwait(false);

        DocumentDefinition documentDefinition = await repository
            .FindByIdAsync(hospitalContext.HospitalId, id, track: true, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Document definition '{id}' was not found.");

        DocumentCatalogWorkflowHelpers.EnsureConcurrency(
            documentDefinition.RowVersion,
            request.RowVersion,
            "document definition");
        DocumentCatalogWorkflowHelpers.EnsureNotRetired(
            documentDefinition.Status,
            "Document definition",
            documentDefinition.Code);

        DateTimeOffset now = timeProvider.GetUtcNow();
        documentDefinition.Status = DocumentDefinitionStatus.Retired;
        documentDefinition.RetiredAt = now;
        documentDefinition.UpdatedAt = now;
        documentDefinition.RowVersion = request.RowVersion + 1;

        auditWriter.Append(
            AuditEntityTypes.DocumentDefinition,
            documentDefinition.Id,
            "document-definition.retired",
            actorId,
            now,
            new
            {
                code = documentDefinition.Code,
                rowVersion = request.RowVersion,
                formVersionId = documentDefinition.FormVersionId,
            });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        return DocumentCatalogMappers.ToDto(documentDefinition);
    }

    private async Task<FormVersion> FindPublishedFormVersionAsync(
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
                $"Form version '{formVersionId}' is not published "
                + "and cannot be assigned to a document definition.");
        }

        return formVersion;
    }
}
