// Application-layer contracts live in this folder, but the namespace stays
// flat as Cynara.Api.Tests: a child namespace would shadow the walking
// resolution existing tests rely on for Cynara.Application.* references.
#pragma warning disable IDE0130 // Namespace does not match folder structure
using System.Net;

using Cynara.Api.Common.ErrorHandling;
using Cynara.Application;
using Cynara.Application.Forms;

namespace Cynara.Api.Tests;

/// <summary>
/// Locks the neutral JSON:API error document the shared
/// <c>CynaraErrorMapping.FromException</c> function produces. The Api layer
/// transports (minimal-API <c>ProblemDetailsMapping</c> and JsonAPI pipeline's
/// <c>CynaraJsonApiExceptionHandler</c>) only wrap this document, so the
/// wire output of every error response stays byte-identical when the mapping
/// is shared between both transports.
/// </summary>
public sealed class CynaraErrorMappingTests
{
    [Fact]
    public void NotFoundException_MapsToNotFoundDocument()
    {
        CynaraErrorDocument document = CynaraErrorMapping.FromException(
            new NotFoundException("not here"));

        Assert.Equal((int)HttpStatusCode.NotFound, document.StatusCode);
        Assert.Equal("Not found", document.Title);
        CynaraErrorItem item = Assert.Single(document.Items);
        Assert.Null(item.Code);
        Assert.Equal("Not found", item.Title);
        Assert.Equal("not here", item.Detail);
        Assert.Null(item.Source);
    }

    [Fact]
    public void ConflictException_MapsToConflictDocument()
    {
        CynaraErrorDocument document = CynaraErrorMapping.FromException(
            new ConflictException("dup"));

        Assert.Equal((int)HttpStatusCode.Conflict, document.StatusCode);
        Assert.Equal("Conflict", document.Title);
        CynaraErrorItem item = Assert.Single(document.Items);
        Assert.Equal("Conflict", item.Title);
        Assert.Equal("dup", item.Detail);
        Assert.Null(item.Source);
    }

    [Fact]
    public void ValidationException_MapsToBadRequestDocument()
    {
        CynaraErrorDocument document = CynaraErrorMapping.FromException(
            new ValidationException("bad"));

        Assert.Equal((int)HttpStatusCode.BadRequest, document.StatusCode);
        Assert.Equal("Validation failed", document.Title);
        CynaraErrorItem item = Assert.Single(document.Items);
        Assert.Equal("Validation failed", item.Title);
        Assert.Equal("bad", item.Detail);
        Assert.Null(item.Source);
    }

    [Fact]
    public void ConcurrencyException_MapsToConflictDocument()
    {
        CynaraErrorDocument document = CynaraErrorMapping.FromException(
            new ConcurrencyException("stale"));

        Assert.Equal((int)HttpStatusCode.Conflict, document.StatusCode);
        Assert.Equal("Concurrency conflict", document.Title);
        CynaraErrorItem item = Assert.Single(document.Items);
        Assert.Equal("Concurrency conflict", item.Title);
        Assert.Equal("stale", item.Detail);
    }

    [Fact]
    public void InvalidStateException_MapsToConflictDocument()
    {
        CynaraErrorDocument document = CynaraErrorMapping.FromException(
            new InvalidStateException("illegal"));

        Assert.Equal((int)HttpStatusCode.Conflict, document.StatusCode);
        Assert.Equal("Invalid state", document.Title);
        CynaraErrorItem item = Assert.Single(document.Items);
        Assert.Equal("Invalid state", item.Title);
        Assert.Equal("illegal", item.Detail);
    }

    [Fact]
    public void TenantContextException_MapsToForbiddenDocument()
    {
        CynaraErrorDocument document = CynaraErrorMapping.FromException(
            new TenantContextException("none"));

        Assert.Equal((int)HttpStatusCode.Forbidden, document.StatusCode);
        Assert.Equal("Tenant context required", document.Title);
        CynaraErrorItem item = Assert.Single(document.Items);
        Assert.Equal("Tenant context required", item.Title);
        Assert.Equal("none", item.Detail);
    }

    [Fact]
    public void NonCynaraException_FallsBackToInternalServerError()
    {
        CynaraErrorDocument document = CynaraErrorMapping.FromException(
            new InvalidOperationException("boom"));

        Assert.Equal((int)HttpStatusCode.InternalServerError, document.StatusCode);
        Assert.Equal("Unexpected error", document.Title);
        CynaraErrorItem item = Assert.Single(document.Items);
        Assert.Equal("Unexpected error", item.Title);
        Assert.Equal("boom", item.Detail);
    }

    [Fact]
    public void UnexpectedHelper_PassesDetailThrough()
    {
        CynaraErrorDocument document = CynaraErrorMapping.Unexpected(
            "See the failure log for details.");

        Assert.Equal((int)HttpStatusCode.InternalServerError, document.StatusCode);
        Assert.Equal("Unexpected error", document.Title);
        CynaraErrorItem item = Assert.Single(document.Items);
        Assert.Equal("See the failure log for details.", item.Detail);
    }

    [Fact]
    public void FormResponseValidationException_ProducesOneItemPerFieldError()
    {
        var fieldErrors = new List<FormResponseFieldError>
        {
            new("REQUIRED_FIELD_MISSING", "/fields/0", "Field is required"),
            new("UNKNOWN_FIELD", "/fields/2", "Field is unknown"),
        };
        CynaraErrorDocument document = CynaraErrorMapping.FromException(
            new FormResponseValidationException(fieldErrors));

        Assert.Equal((int)HttpStatusCode.BadRequest, document.StatusCode);
        Assert.Equal("Validation failed", document.Title);
        Assert.Equal(2, document.Items.Count);
        Assert.All(document.Items, item => Assert.Equal("Validation failed", item.Title));
        Assert.All(document.Items, item => Assert.NotNull(item.Source));
    }

    [Fact]
    public void FormResponseValidationException_SourceExposesBothPointerForms()
    {
        var fieldErrors = new List<FormResponseFieldError>
        {
            new("REQUIRED_FIELD_MISSING", "/fields/0", "Field is required"),
        };
        CynaraErrorDocument document = CynaraErrorMapping.FromException(
            new FormResponseValidationException(fieldErrors));

        CynaraErrorItem item = Assert.Single(document.Items);
        Assert.NotNull(item.Source);
        Assert.Equal("/fields/0", item.Source.JsonApiPointer);
        Assert.Equal(
            "/data/attributes/answersJson//fields/0",
            item.Source.MinimalApiPointer);
    }

    [Fact]
    public void FormResponseValidationException_BlankPathLeavesSourceNull()
    {
        var fieldErrors = new List<FormResponseFieldError>
        {
            new("GENERIC", " ", "broken"),
        };
        CynaraErrorDocument document = CynaraErrorMapping.FromException(
            new FormResponseValidationException(fieldErrors));

        CynaraErrorItem item = Assert.Single(document.Items);
        Assert.Null(item.Source);
    }

    [Fact]
    public void FormResponseValidationException_DefaultCtorIsEmptyDocument()
    {
        CynaraErrorDocument document = CynaraErrorMapping.FromException(
            new FormResponseValidationException());

        Assert.Equal((int)HttpStatusCode.BadRequest, document.StatusCode);
        Assert.Equal("Validation failed", document.Title);
        Assert.Empty(document.Items);
    }

    [Fact]
    public void FormResponseValidationException_PreservesCodeAndDetailPerItem()
    {
        var fieldErrors = new List<FormResponseFieldError>
        {
            new("REQUIRED_FIELD_MISSING", "/fields/0", "Field is required"),
            new("UNKNOWN_FIELD", "/fields/2", "Field is unknown"),
        };
        CynaraErrorDocument document = CynaraErrorMapping.FromException(
            new FormResponseValidationException(fieldErrors));

        Assert.Collection(
            document.Items,
            first =>
            {
                Assert.Equal("REQUIRED_FIELD_MISSING", first.Code);
                Assert.Equal("Field is required", first.Detail);
            },
            second =>
            {
                Assert.Equal("UNKNOWN_FIELD", second.Code);
                Assert.Equal("Field is unknown", second.Detail);
            });
    }

    [Fact]
    public void PlainCynaraException_NeverCarriesSource()
    {
        CynaraException[] plain =
        [
            new NotFoundException("x"),
            new ConflictException("x"),
            new ValidationException("x"),
            new ConcurrencyException("x"),
            new InvalidStateException("x"),
            new TenantContextException("x"),
        ];

        foreach (CynaraException exception in plain)
        {
            CynaraErrorDocument document = CynaraErrorMapping.FromException(exception);
            CynaraErrorItem item = Assert.Single(document.Items);
            Assert.True(
                item.Source is null,
                $"{exception.GetType().Name} must not carry a source.");
        }
    }
}
