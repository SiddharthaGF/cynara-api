using Cynara.Application;
using Cynara.Application.Modules.Documents;
using Cynara.Domain.Documents;

using ValidationException = System.ComponentModel.DataAnnotations.ValidationException;

namespace Cynara.Api.Tests.Documents.UnitTests;

/// <summary>
/// Unit coverage for the shared validation helpers used by the document
/// catalog workflows. Each helper enforces a single invariant from the
/// CYN-36 contract and must surface the same exception type that the
/// application exception handler maps to HTTP.
/// </summary>
public sealed class DocumentCatalogWorkflowHelpersTests
{
    [Fact]
    public void EnsureValidCode_RejectsNull()
    {
        Assert.Throws<ValidationException>(
            () => DocumentCatalogWorkflowHelpers.EnsureValidCode(null!, "Document definition"));
    }

    [Fact]
    public void EnsureValidCode_RejectsWhitespace()
    {
        Assert.Throws<ValidationException>(
            () => DocumentCatalogWorkflowHelpers.EnsureValidCode("   ", "Document definition"));
    }

    [Fact]
    public void EnsureValidCode_RejectsEmptyString()
    {
        Assert.Throws<ValidationException>(
            () => DocumentCatalogWorkflowHelpers.EnsureValidCode(string.Empty, "Document definition"));
    }

    [Fact]
    public void EnsureValidCode_RejectsTooLong()
    {
        string code = new('a', 65);

        Assert.Throws<ValidationException>(
            () => DocumentCatalogWorkflowHelpers.EnsureValidCode(code, "Document definition"));
    }

    [Fact]
    public void EnsureValidCode_AcceptsCodeAtBoundary()
    {
        Exception? exception = Record.Exception(
            () => DocumentCatalogWorkflowHelpers.EnsureValidCode("a", "Document definition"));

        Assert.Null(exception);
    }

    [Fact]
    public void EnsureValidCode_MentionsEntityNameInMessage()
    {
        ValidationException exception = Assert.Throws<ValidationException>(
            () => DocumentCatalogWorkflowHelpers.EnsureValidCode("   ", "CustomEntity"));

        Assert.Contains("CustomEntity", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureConcurrency_PassesWhenEqual()
    {
        Exception? exception = Record.Exception(
            () => DocumentCatalogWorkflowHelpers.EnsureConcurrency(5u, 5u, "document definition"));

        Assert.Null(exception);
    }

    [Fact]
    public void EnsureConcurrency_ThrowsWhenDifferent()
    {
        Assert.Throws<ConcurrencyException>(
            () => DocumentCatalogWorkflowHelpers.EnsureConcurrency(5u, 4u, "document definition"));
    }

    [Fact]
    public void EnsureConcurrency_MentionsEntityNameInMessage()
    {
        ConcurrencyException exception = Assert.Throws<ConcurrencyException>(
            () => DocumentCatalogWorkflowHelpers.EnsureConcurrency(1u, 2u, "Document definition"));

        Assert.Contains("Document definition", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureNotRetired_PassesWhenActive()
    {
        Exception? exception = Record.Exception(
            () => DocumentCatalogWorkflowHelpers.EnsureNotRetired(
                DocumentDefinitionStatus.Active,
                "Document definition",
                "code"));

        Assert.Null(exception);
    }

    [Fact]
    public void EnsureNotRetired_ThrowsWhenRetired()
    {
        InvalidStateException exception = Assert.Throws<InvalidStateException>(
            () => DocumentCatalogWorkflowHelpers.EnsureNotRetired(
                DocumentDefinitionStatus.Retired,
                "Document definition",
                "code"));

        Assert.Contains("code", exception.Message, StringComparison.Ordinal);
        Assert.Contains("retired", exception.Message, StringComparison.Ordinal);
    }
}
