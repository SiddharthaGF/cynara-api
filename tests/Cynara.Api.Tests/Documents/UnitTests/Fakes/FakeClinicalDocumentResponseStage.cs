using Cynara.Application;
using Cynara.Application.Forms;
using Cynara.Application.Modules.Documents;
using Cynara.Domain.Forms;

namespace Cynara.Api.Tests.Documents.UnitTests.Fakes;

/// <summary>
/// In-memory fake of <see cref="IClinicalDocumentResponseStage"/> for unit
/// tests that exercise the document lifecycle without the EF Core stack. It
/// composes the response repository fake for lookups and staging and the
/// fake validator for completion-mode validation.
/// </summary>
public sealed class FakeClinicalDocumentResponseStage(
    FakeFormResponseRepository responses,
    IFormResponseValidator validator) : IClinicalDocumentResponseStage
{
    public Task<FormResponse> RequireResponseAsync(
        Guid responseId,
        bool track,
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        FormResponse? match = responses.Responses.SingleOrDefault(
            item => item.Id == responseId && item.HospitalId == hospitalId);
        return Task.FromResult(match ?? throw new NotFoundException(
            $"Form response '{responseId}' was not found."));
    }

    public string ValidateAndNormalizeAnswers(
        FormVersion formVersion,
        string answersJson,
        FormResponseValidationMode mode)
    {
        FormResponseValidationResult result = validator.Validate(
            clinicalSchemaJson: "{}",
            uiSchemaJson: null,
            rulesSchemaJson: null,
            answersJson,
            mode);
        result.EnsureValid();
        return result.NormalizedAnswersJson;
    }

    public void Add(FormResponse response, FormResponseRevision revision)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(revision);
        responses.Add(response, revision);
    }

    public void AddRevision(FormResponseRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        responses.AddRevision(revision);
    }
}
