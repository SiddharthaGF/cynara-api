using Cynara.Application.Forms;
using Cynara.Application.Modules.FormResponses.Persistence;
using Cynara.Domain.Forms;

namespace Cynara.Application.Modules.Documents;

/// <summary>
/// Default implementation of <see cref="IClinicalDocumentResponseStage"/>.
/// Composes the form response repository with the schema validator so the
/// clinical document lifecycle can validate answers in complete mode before
/// freezing the recorded content in the same unit of work.
/// </summary>
public sealed class ClinicalDocumentResponseStage(
    IFormResponseRepository responses,
    IFormResponseValidator validator) : IClinicalDocumentResponseStage
{
    /// <inheritdoc />
    public async Task<FormResponse> RequireResponseAsync(
        Guid responseId,
        bool track,
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        return await responses.FindByIdAsync(
                responseId,
                track,
                includeDeleted: false,
                hospitalId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Form response '{responseId}' was not found.");
    }

    /// <inheritdoc />
    public string ValidateAndNormalizeAnswers(
        FormVersion formVersion,
        string answersJson,
        FormResponseValidationMode mode)
    {
        ArgumentNullException.ThrowIfNull(formVersion);
        FormResponseValidationResult validation = validator.Validate(
            formVersion.ClinicalSchemaJson,
            formVersion.UiSchemaJson,
            formVersion.RulesSchemaJson,
            answersJson,
            mode);
        validation.EnsureValid();
        return validation.NormalizedAnswersJson;
    }

    /// <inheritdoc />
    public void Add(FormResponse response, FormResponseRevision revision)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(revision);
        responses.Add(response, revision);
    }

    /// <inheritdoc />
    public void AddRevision(FormResponseRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        responses.AddRevision(revision);
    }
}
