using Cynara.Application.Forms;
using Cynara.Domain.Forms;

namespace Cynara.Application.Modules.Documents;

/// <summary>
/// Staging support for the form response bound to a clinical document:
/// lookup, completion-mode validation, and change staging behind one port,
/// so repositories only stage while the workflow owns the commit.
/// </summary>
public interface IClinicalDocumentResponseStage
{
    /// <summary>
    /// Returns the tracked form response matching the supplied identifier in
    /// the resolved hospital workspace, or throws when unknown.
    /// </summary>
    public Task<FormResponse> RequireResponseAsync(
        Guid responseId,
        bool track,
        Guid hospitalId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Validates <paramref name="answersJson"/> against the published form
    /// version in the supplied mode and returns the normalized answers. Throws
    /// <see cref="FormResponseValidationException"/> on field errors.
    /// </summary>
    public string ValidateAndNormalizeAnswers(
        FormVersion formVersion,
        string answersJson,
        FormResponseValidationMode mode);

    /// <summary>Stages a new response and its initial revision.</summary>
    public void Add(FormResponse response, FormResponseRevision revision);

    /// <summary>Stages a new revision for the bound response.</summary>
    public void AddRevision(FormResponseRevision revision);
}
