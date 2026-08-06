using Cynara.Application.Forms;

namespace Cynara.Api.Tests.Documents.UnitTests.Fakes;

/// <summary>
/// In-memory fake of <see cref="IFormResponseValidator"/> for unit tests
/// that exercise the document lifecycle without the schema engine. Answers
/// pass through untouched and valid; integration tests cover the real
/// validator.
/// </summary>
public sealed class FakeFormResponseValidator : IFormResponseValidator
{
    public FormResponseValidationResult Validate(
        string clinicalSchemaJson,
        string? uiSchemaJson,
        string? rulesSchemaJson,
        string answersJson,
        FormResponseValidationMode mode)
    {
        return new FormResponseValidationResult(answersJson, []);
    }
}
