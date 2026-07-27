// The folder groups Application-layer contracts together; the namespace stays
// flat as `Cynara.Api.Tests` so it never shadows `Application.Forms.*`
// references that existing test files resolve via namespace walking from
// `Cynara.Api.Tests` up to `Cynara.Application.Forms`. Adding a child
// namespace named `Cynara.Api.Tests.Application` would break that walking
// resolution for unrelated tests in the same project.
#pragma warning disable IDE0130 // Namespace does not match folder structure
using System.Net;
using System.Reflection;

using Cynara.Api.Common.ErrorHandling;
using Cynara.Application;
using Cynara.Application.Forms;

// The folder groups Application-layer contracts together; the namespace stays
// flat as `Cynara.Api.Tests` so it never shadows `Application.Forms.*`
// references that existing test files resolve via namespace walking from
// `Cynara.Api.Tests` up to `Cynara.Application.Forms`. Adding a child
// namespace named `Cynara.Api.Tests.Application` would break that walking
// resolution for unrelated tests in the same project.
namespace Cynara.Api.Tests;

/// <summary>
/// Locks the wire contract of <see cref="CynaraException"/> subtypes: each
/// subtype must declare the HTTP status and JSON:API title the shared error
/// mapping uses. The values match the documented behavior in the Api layer
/// error handlers (<see cref="ProblemDetailsMapping"/> and
/// <c>CynaraJsonApiExceptionHandler</c>) and must remain stable so the HTTP
/// responses keep their byte-identical shape.
/// </summary>
public sealed class CynaraExceptionMetadataTests
{
    [Fact]
    public void BaseException_IsAbstractAndExposesStatus()
    {
        Type type = typeof(CynaraException);
        Assert.True(type.IsAbstract);
        PropertyInfo status = Assert.Single(
            type.GetProperties(),
            static p => string.Equals(p.Name, nameof(CynaraException.StatusCode), StringComparison.Ordinal));
        Assert.True(
            status.GetMethod!.IsAbstract,
            "StatusCode must be abstract so every subtype is forced to declare one.");
        Assert.Equal(typeof(HttpStatusCode), status.PropertyType);
    }

    [Fact]
    public void BaseException_IsAbstractAndExposesTitle()
    {
        Type type = typeof(CynaraException);
        PropertyInfo title = Assert.Single(
            type.GetProperties(),
            static p => string.Equals(p.Name, nameof(CynaraException.Title), StringComparison.Ordinal));
        Assert.True(
            title.GetMethod!.IsAbstract,
            "Title must be abstract so every subtype is forced to declare one.");
        Assert.Equal(typeof(string), title.PropertyType);
    }

    [Fact]
    public void NotFoundException_ReportsStatusAndTitle()
    {
        CynaraException exception = new NotFoundException("missing");

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("Not found", exception.Title);
    }

    [Fact]
    public void ConflictException_ReportsStatusAndTitle()
    {
        CynaraException exception = new ConflictException("dup");

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Equal("Conflict", exception.Title);
    }

    [Fact]
    public void ValidationException_ReportsStatusAndTitle()
    {
        CynaraException exception = new ValidationException("bad");

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal("Validation failed", exception.Title);
    }

    [Fact]
    public void ConcurrencyException_ReportsStatusAndTitle()
    {
        CynaraException exception = new ConcurrencyException("stale");

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Equal("Concurrency conflict", exception.Title);
    }

    [Fact]
    public void InvalidStateException_ReportsStatusAndTitle()
    {
        CynaraException exception = new InvalidStateException("illegal");

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Equal("Invalid state", exception.Title);
    }

    [Fact]
    public void TenantContextException_ReportsStatusAndTitle()
    {
        CynaraException exception = new TenantContextException("none");

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Equal("Tenant context required", exception.Title);
    }

    [Fact]
    public void FormResponseValidationException_ReportsStatusAndTitleAndKeepsErrors()
    {
        var fieldErrors = new List<FormResponseFieldError>
        {
            new("REQUIRED_FIELD_MISSING", "/fields/0", "Field is required"),
        };
        var exception = new FormResponseValidationException(fieldErrors);

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal("Validation failed", exception.Title);
        Assert.Same(fieldErrors, exception.Errors);
    }

    [Fact]
    public void BaseException_ForcesEveryKnownSubtypeToDeclareMetadata()
    {
        Type[] concreteSubtypes = [.. typeof(CynaraException).Assembly
            .GetTypes()
            .Where(static t => t is { IsClass: true, IsAbstract: false }
                && t.IsSubclassOf(typeof(CynaraException)))];

        Assert.NotEmpty(concreteSubtypes);

        foreach (Type subtype in concreteSubtypes)
        {
            PropertyInfo? status = subtype.GetProperty(
                nameof(CynaraException.StatusCode));
            PropertyInfo? title = subtype.GetProperty(
                nameof(CynaraException.Title));

            Assert.False(
                status?.GetMethod?.IsAbstract ?? true,
                $"{subtype.FullName} must override StatusCode.");
            Assert.False(
                title?.GetMethod?.IsAbstract ?? true,
                $"{subtype.FullName} must override Title.");
        }
    }
}
