namespace Cynara.Application.Forms;

public interface IFormResponseValidator
{
    public FormResponseValidationResult Validate(
        string clinicalSchemaJson,
        string? uiSchemaJson,
        string? rulesSchemaJson,
        string answersJson,
        FormResponseValidationMode mode);
}
