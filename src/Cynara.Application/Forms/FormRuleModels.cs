using System.Text.Json.Nodes;

namespace Cynara.Application.Forms;

public sealed record RuleValidationError(string Code, string Message);

public sealed record FormRuleEvaluationResult(
    IReadOnlyDictionary<string, bool> Visibility,
    IReadOnlyDictionary<string, bool> Enabled,
    IReadOnlyDictionary<string, bool> Required,
    IReadOnlyDictionary<string, object?> CalculatedValues,
    IReadOnlyList<RuleValidationError> ValidationErrors);

public sealed record RuleDependencyMetadata(
    IReadOnlyList<string> CalculatedFieldIds,
    IReadOnlyList<string> EvaluationOrder);
