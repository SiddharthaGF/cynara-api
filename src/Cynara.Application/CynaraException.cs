
using Cynara.Application.Forms;

namespace Cynara.Application;

public abstract class CynaraException(string message) : Exception(message);

public sealed class NotFoundException(string message) : CynaraException(message);

public sealed class ConflictException(string message) : CynaraException(message);

public sealed class ValidationException(string message) : CynaraException(message);

public sealed class ConcurrencyException(string message) : CynaraException(message);

public sealed class InvalidStateException(string message) : CynaraException(message);

public sealed class FormResponseValidationException(IReadOnlyList<FormResponseFieldError> errors) : CynaraException(errors.Count == 1
            ? errors[0].Message
            : $"{errors.Count} validation errors occurred.")
{
    public IReadOnlyList<FormResponseFieldError> Errors { get; } = errors;
}
