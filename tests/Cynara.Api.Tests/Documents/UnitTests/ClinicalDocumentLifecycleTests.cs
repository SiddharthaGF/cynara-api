using Cynara.Application;
using Cynara.Application.Modules.Documents;
using Cynara.Domain.Documents;

namespace Cynara.Api.Tests.Documents.UnitTests;

/// <summary>
/// Unit coverage for the clinical document lifecycle state machine.
/// </summary>
public sealed class ClinicalDocumentLifecycleTests
{
    [Fact]
    public void Fire_Complete_FromInProgress_TransitionsToCompleted()
    {
        ClinicalDocument document = Document();
        ClinicalDocumentLifecycle.Fire(
            document, ClinicalDocumentLifecycle.Trigger.Complete);
        Assert.Equal(ClinicalDocumentStatus.Completed, document.Status);
    }

    [Fact]
    public void Fire_Cancel_FromInProgress_TransitionsToCanceled()
    {
        ClinicalDocument document = Document();
        ClinicalDocumentLifecycle.Fire(
            document, ClinicalDocumentLifecycle.Trigger.Cancel);
        Assert.Equal(ClinicalDocumentStatus.Canceled, document.Status);
    }

    [Fact]
    public void Fire_EnterInError_FromInProgress_TransitionsToEnteredInError()
    {
        ClinicalDocument document = Document();
        ClinicalDocumentLifecycle.Fire(
            document, ClinicalDocumentLifecycle.Trigger.EnterInError);
        Assert.Equal(ClinicalDocumentStatus.EnteredInError, document.Status);
    }

    [Fact]
    public void Fire_EnterInError_FromCompleted_TransitionsToEnteredInError()
    {
        ClinicalDocument document = Document(ClinicalDocumentStatus.Completed);
        ClinicalDocumentLifecycle.Fire(
            document, ClinicalDocumentLifecycle.Trigger.EnterInError);
        Assert.Equal(ClinicalDocumentStatus.EnteredInError, document.Status);
    }

    [Fact]
    public void Fire_Cancel_FromCompleted_ThrowsInvalidState()
    {
        ClinicalDocument document = Document(ClinicalDocumentStatus.Completed);

        InvalidStateException ex = Assert.Throws<InvalidStateException>(
            () => ClinicalDocumentLifecycle.Fire(
                document, ClinicalDocumentLifecycle.Trigger.Cancel));

        Assert.Contains("completed", ex.Message, StringComparison.Ordinal);
        Assert.Equal(ClinicalDocumentStatus.Completed, document.Status);
    }

    [Fact]
    public void Fire_FromCanceled_ThrowsInvalidState()
    {
        ClinicalDocument document = Document(ClinicalDocumentStatus.Canceled);

        Assert.Throws<InvalidStateException>(
            () => ClinicalDocumentLifecycle.Fire(
                document, ClinicalDocumentLifecycle.Trigger.Complete));
        Assert.Throws<InvalidStateException>(
            () => ClinicalDocumentLifecycle.Fire(
                document, ClinicalDocumentLifecycle.Trigger.EnterInError));
        Assert.Equal(ClinicalDocumentStatus.Canceled, document.Status);
    }

    [Fact]
    public void Fire_FromEnteredInError_ThrowsInvalidState()
    {
        ClinicalDocument document = Document(ClinicalDocumentStatus.EnteredInError);

        Assert.Throws<InvalidStateException>(
            () => ClinicalDocumentLifecycle.Fire(
                document, ClinicalDocumentLifecycle.Trigger.EnterInError));
        Assert.Equal(ClinicalDocumentStatus.EnteredInError, document.Status);
    }

    private static ClinicalDocument Document(
        ClinicalDocumentStatus status = ClinicalDocumentStatus.InProgress)
    {
        return new ClinicalDocument { Status = status };
    }
}
