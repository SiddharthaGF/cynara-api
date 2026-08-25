using System.Net;

using Cynara.Application.Forms;

namespace Cynara.Application;

/// <summary>
/// Base type for all application-level exceptions. Subtypes declare the HTTP
/// status and JSON:API title that both error pipelines (minimal-API and
/// JsonAPI) read, so every error response stays identical regardless of
/// which endpoint raised the exception.
/// </summary>
public abstract class CynaraException : Exception
{
    protected CynaraException()
    {
    }

    protected CynaraException(string message)
        : base(message)
    {
    }

    protected CynaraException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// HTTP status code that the shared error mapping emits when this
    /// exception reaches the wire. Subclasses must declare their canonical
    /// status (e.g. <see cref="HttpStatusCode.NotFound"/>,
    /// <see cref="HttpStatusCode.Conflict"/>).
    /// </summary>
    public abstract HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Human-readable title used in the JSON:API error envelope (the
    /// <c>title</c> field). Subclasses must declare the canonical title so
    /// both the minimal-API and JsonAPI error pipelines stay in sync.
    /// </summary>
    public abstract string Title { get; }
}

public sealed class NotFoundException : CynaraException
{
    public NotFoundException()
    {
    }

    public NotFoundException(string message)
        : base(message)
    {
    }

    public NotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public override HttpStatusCode StatusCode => HttpStatusCode.NotFound;

    public override string Title => "Not found";
}

public sealed class ConflictException : CynaraException
{
    public ConflictException()
    {
    }

    public ConflictException(string message)
        : base(message)
    {
    }

    public ConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public override HttpStatusCode StatusCode => HttpStatusCode.Conflict;

    public override string Title => "Conflict";
}

public sealed class ValidationException : CynaraException
{
    public ValidationException()
    {
    }

    public ValidationException(string message)
        : base(message)
    {
    }

    public ValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public override HttpStatusCode StatusCode => HttpStatusCode.BadRequest;

    public override string Title => "Validation failed";
}

public sealed class ConcurrencyException : CynaraException
{
    /// <summary>
    /// Canonical wire title, also reused by the Api-layer mapping of raw EF
    /// optimistic-concurrency failures so every 409 document stays identical.
    /// </summary>
    public const string CanonicalTitle = "Concurrency conflict";

    public ConcurrencyException()
    {
    }

    public ConcurrencyException(string message)
        : base(message)
    {
    }

    public ConcurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public override HttpStatusCode StatusCode => HttpStatusCode.Conflict;

    public override string Title => CanonicalTitle;
}

public sealed class InvalidStateException : CynaraException
{
    public InvalidStateException()
    {
    }

    public InvalidStateException(string message)
        : base(message)
    {
    }

    public InvalidStateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public override HttpStatusCode StatusCode => HttpStatusCode.Conflict;

    public override string Title => "Invalid state";
}

public sealed class FormResponseValidationException : CynaraException
{
    public FormResponseValidationException()
    {
    }

    public FormResponseValidationException(string message)
        : base(message)
    {
    }

    public FormResponseValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public FormResponseValidationException(IReadOnlyList<FormResponseFieldError> errors)
        : base((errors ?? throw new ArgumentNullException(nameof(errors))).Count == 1
            ? errors[0].Message
            : $"{errors.Count} validation errors occurred.")
    {
        Errors = errors;
    }

    public IReadOnlyList<FormResponseFieldError> Errors { get; } = [];

    public override HttpStatusCode StatusCode => HttpStatusCode.BadRequest;

    public override string Title => "Validation failed";
}

/// <summary>
/// Raised when a request cannot be associated with a known, active hospital
/// workspace. Maps to 403 by default; the workspace bootstrap may decide the
/// specific response based on the failure cause.
/// </summary>
public sealed class TenantContextException : CynaraException
{
    public TenantContextException()
    {
    }

    public TenantContextException(string message)
        : base(message)
    {
    }

    public TenantContextException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public override HttpStatusCode StatusCode => HttpStatusCode.Forbidden;

    public override string Title => "Tenant context required";
}
