namespace Cynara.Application.Forms;

public interface IFormRuleEngine
{
    public FormRuleEvaluationResult Evaluate(
        string clinicalSchemaJson,
        string rulesSchemaJson,
        IReadOnlyDictionary<string, object?> values,
        string? uiSchemaJson = null);
}
