namespace Cynara.Application.Forms;

public enum FormResponseValidationMode
{
    Draft,
    Complete,
}

public sealed record FormResponseFieldError(string Code, string Path, string Message);

public sealed record FormResponseValidationResult(
    string NormalizedAnswersJson,
    IReadOnlyList<FormResponseFieldError> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public void EnsureValid()
    {
        if (!IsValid)
        {
            throw new FormResponseValidationException(Errors);
        }
    }
}
