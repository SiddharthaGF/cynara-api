using Cynara.Application.Forms;

namespace Cynara.Application;

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
}

public sealed class ConcurrencyException : CynaraException
{
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
}
