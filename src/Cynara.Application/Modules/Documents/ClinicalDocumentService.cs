using Cynara.Application.Audit;
using Cynara.Application.Forms;
using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.Documents.Persistence;
using Cynara.Application.Modules.FormResponses;
using Cynara.Application.Modules.Tasks;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Documents;
using Cynara.Domain.Encounters;
using Cynara.Domain.Forms;

namespace Cynara.Application.Modules.Documents;

/// <summary>
/// Default implementation of <see cref="IClinicalDocumentService"/>. Start
/// delegates reference resolution to
/// <see cref="IClinicalDocumentReferenceResolver"/> and enforces catalog
/// multiplicity; transitions complete the bound response atomically and audit.
/// </summary>
public sealed class ClinicalDocumentService(
    IClinicalDocumentRepository documents,
    IClinicalDocumentReferenceResolver references,
    IClinicalDocumentResponseStage responses,
    IClinicalDocumentTaskCloser taskCloser,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IWorkflowContext context,
    ICapabilityGuard capabilityGuard) : IClinicalDocumentService
{
    public async Task<ClinicalDocumentDto> StartAsync(
        StartClinicalDocumentRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        context.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.ClinicalDocumentsWrite, cancellationToken)
            .ConfigureAwait(false);
        Guid hospitalId = context.HospitalId;

        ClinicalDocumentWorkflowHelpers.EnsureValidAuthorId(actorId);
        DocumentDefinition definition = await references
            .RequireActiveDefinitionAsync(
                hospitalId, request.DocumentDefinitionId, cancellationToken)
            .ConfigureAwait(false);
        Encounter encounter = await references
            .RequireOpenEncounterAsync(
                hospitalId, request.EncounterId, cancellationToken)
            .ConfigureAwait(false);
        FormVersion formVersion = await references
            .RequirePublishedFormVersionAsync(
                hospitalId, definition.FormVersionId, cancellationToken)
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

        DateTimeOffset now = context.GetUtcNow();
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
        context.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.ClinicalDocumentsRead, cancellationToken)
            .ConfigureAwait(false);
        ClinicalDocument document = await documents
            .FindByIdAsync(
                context.HospitalId, id, track: false, cancellationToken)
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
        context.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.ClinicalDocumentsRead, cancellationToken)
            .ConfigureAwait(false);

        ClinicalDocumentListCriteria criteria = new(
            request.EncounterId,
            request.PatientId,
            request.DocumentDefinitionId,
            ClinicalDocumentWorkflowHelpers.ParseStatusOrNull(request.Status));

        IReadOnlyList<ClinicalDocument> matches = await documents
            .ListAsync(context.HospitalId, criteria, cancellationToken)
            .ConfigureAwait(false);
        return [.. matches.Select(ClinicalDocumentMappers.ToDto)];
    }

    /// <inheritdoc />
    public async Task<ClinicalDocumentDto> CompleteAsync(
        Guid id,
        TransitionClinicalDocumentRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        context.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.ClinicalDocumentsWrite, cancellationToken)
            .ConfigureAwait(false);

        ClinicalDocument document = await documents
            .FindByIdAsync(
                context.HospitalId,
                id,
                track: true,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Clinical document '{id}' was not found.");
        ClinicalDocumentWorkflowHelpers.EnsureConcurrency(
            document.RowVersion, request.RowVersion);

        DocumentDefinition definition = await references
            .RequireDefinitionAsync(
                context.HospitalId,
                document.DocumentDefinitionId,
                cancellationToken)
            .ConfigureAwait(false);
        if (definition.RequiresActorForCompletion
            && string.IsNullOrWhiteSpace(actorId))
        {
            throw new ValidationException(
                $"Catalog entry '{definition.Code}' requires an "
                + "authenticated actor to complete documents.");
        }

        FormResponse response = await responses.RequireResponseAsync(
                document.FormResponseId,
                track: true,
                context.HospitalId,
                cancellationToken)
            .ConfigureAwait(false);
        FormResponseWorkflowHelpers.EnsureDraft(response);
        response.AnswersJson = responses.ValidateAndNormalizeAnswers(
            response.FormVersion,
            response.AnswersJson,
            FormResponseValidationMode.Complete);

        DateTimeOffset now = context.GetUtcNow();
        ClinicalDocumentLifecycle.Fire(
            document, TerminalLifecycle.Trigger.Complete);
        FormResponseLifecycle.Fire(
            response, FormResponseLifecycle.Trigger.Complete);
        response.RevisionNumber++;
        response.RowVersion++;
        response.CompletedAt = now;
        response.UpdatedAt = now;
        responses.AddRevision(FormResponseWorkflowHelpers.CreateRevision(
            response, actorId, now));

        document.CompletedAt = now;
        document.UpdatedAt = now;
        document.RowVersion = request.RowVersion + 1;

        auditWriter.Append(
            AuditEntityTypes.ClinicalDocument,
            document.Id,
            "document.completed",
            actorId,
            now,
            new
            {
                status = ClinicalDocumentWorkflowHelpers.FormatStatus(
                    document.Status),
                completedAt = document.CompletedAt,
                formResponseId = document.FormResponseId,
                revisionNumber = response.RevisionNumber,
                rowVersion = request.RowVersion,
            });

        await taskCloser.CloseOpenTasksForCompletedDocumentAsync(
                document.HospitalId,
                document.EncounterId,
                definition.Code,
                document.Id,
                actorId,
                now,
                cancellationToken)
            .ConfigureAwait(false);

        _ = await unitOfWork.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        return ClinicalDocumentMappers.ToDto(document);
    }

    /// <inheritdoc />
    public async Task<ClinicalDocumentDto> CancelAsync(
        Guid id,
        TransitionClinicalDocumentRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        return await TransitionAsync(
            id,
            request,
            actorId,
            TerminalLifecycle.Trigger.Cancel,
            "document.canceled",
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ClinicalDocumentDto> EnterInErrorAsync(
        Guid id,
        TransitionClinicalDocumentRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        return await TransitionAsync(
            id,
            request,
            actorId,
            TerminalLifecycle.Trigger.EnterInError,
            "document.enteredInError",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ClinicalDocumentDto> TransitionAsync(
        Guid id,
        TransitionClinicalDocumentRequest request,
        string? actorId,
        TerminalLifecycle.Trigger trigger,
        string auditAction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        context.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.ClinicalDocumentsWrite, cancellationToken)
            .ConfigureAwait(false);

        ClinicalDocument document = await documents
            .FindByIdAsync(
                context.HospitalId,
                id,
                track: true,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Clinical document '{id}' was not found.");
        ClinicalDocumentWorkflowHelpers.EnsureConcurrency(
            document.RowVersion, request.RowVersion);

        bool enteredInError = trigger == TerminalLifecycle.Trigger.EnterInError;
        string? enteredInErrorReason = null;
        if (enteredInError)
        {
            enteredInErrorReason =
                ClinicalDocumentWorkflowHelpers.EnsureEnteredInErrorReason(
                    request.Reason);
            actorId = ClinicalDocumentWorkflowHelpers
                .EnsureEnteredInErrorActor(actorId);
        }

        DateTimeOffset now = context.GetUtcNow();
        ClinicalDocumentLifecycle.Fire(document, trigger);

        if (enteredInError)
        {
            document.EnteredInErrorReason = enteredInErrorReason;
            document.EnteredInErrorById = actorId;
            document.EnteredInErrorAt = now;
        }
        else
        {
            document.CanceledAt = now;
        }

        document.UpdatedAt = now;
        document.RowVersion = request.RowVersion + 1;

        auditWriter.Append(
            AuditEntityTypes.ClinicalDocument,
            document.Id,
            auditAction,
            actorId,
            now,
            new
            {
                status = ClinicalDocumentWorkflowHelpers.FormatStatus(
                    document.Status),
                reason = document.EnteredInErrorReason,
                rowVersion = request.RowVersion,
            });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        return ClinicalDocumentMappers.ToDto(document);
    }
}
