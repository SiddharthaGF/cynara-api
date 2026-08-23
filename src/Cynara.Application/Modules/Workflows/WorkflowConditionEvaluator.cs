using System.Globalization;
using System.Text.Json;

namespace Cynara.Application.Modules.Workflows;

/// <summary>
/// Server-side evaluator for workflow transition conditions — restricted
/// declarative expressions of field refs, literals, comparisons, boolean
/// combinators, and utilities. Only boolean results are meaningful for
/// guards; malformed expressions throw <see cref="ValidationException"/>.
/// </summary>
internal static class WorkflowConditionEvaluator
{
    /// <summary>Evaluates a condition expression against the supplied workflow inputs.</summary>
    public static bool Evaluate(
        JsonElement condition,
        IReadOnlyDictionary<string, JsonElement>? values)
    {
        return ToBoolean(EvaluateNode(condition, values));
    }

    private static object? EvaluateNode(
        JsonElement node,
        IReadOnlyDictionary<string, JsonElement>? values)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("ref", out JsonElement refElement))
            {
                string code = refElement.GetString() ?? string.Empty;
                return values is not null
                    && values.TryGetValue(code, out JsonElement value)
                    ? ToObject(value)
                    : null;
            }

            if (node.TryGetProperty("lit", out JsonElement literal))
            {
                return ToObject(literal);
            }

            if (!node.TryGetProperty("op", out JsonElement opElement)
                || opElement.ValueKind != JsonValueKind.String)
            {
                throw new ValidationException(
                    "Workflow condition node is missing the 'op' operator.");
            }

            string op = opElement.GetString()!;
            if (!node.TryGetProperty("args", out JsonElement argsElement)
                || argsElement.ValueKind != JsonValueKind.Array)
            {
                throw new ValidationException(
                    $"Workflow condition operator '{op}' is missing 'args'.");
            }

            JsonElement[] args = [.. argsElement.EnumerateArray()];
            return EvaluateOperator(op, args, values);
        }

        throw new ValidationException(
            "Workflow condition must be a field reference, literal, or "
            + "operator expression.");
    }

    /// <summary>Dispatches a validated operator expression to its evaluator.</summary>
    /// <exception cref="ValidationException">
    /// Thrown when the operator is not part of the supported condition
    /// vocabulary.
    /// </exception>
    private static object? EvaluateOperator(
        string op,
        JsonElement[] args,
        IReadOnlyDictionary<string, JsonElement>? values)
    {
        return op switch
        {
            "eq" => Compare(
                args,
                values,
                static (left, right) => CompareValues(left, right) == 0),
            "neq" => Compare(
                args,
                values,
                static (left, right) => CompareValues(left, right) != 0),
            "gt" => Compare(
                args,
                values,
                static (left, right) => CompareValues(left, right) > 0),
            "gte" => Compare(
                args,
                values,
                static (left, right) => CompareValues(left, right) >= 0),
            "lt" => Compare(
                args,
                values,
                static (left, right) => CompareValues(left, right) < 0),
            "lte" => Compare(
                args,
                values,
                static (left, right) => CompareValues(left, right) <= 0),
            "and" => args.All(
                argument => ToBoolean(EvaluateNode(argument, values))),
            "or" => args.Any(
                argument => ToBoolean(EvaluateNode(argument, values))),
            "not" => !ToBoolean(EvaluateNode(args[0], values)),
            "empty" => IsEmpty(EvaluateNode(args[0], values)),
            "coalesce" => Coalesce(args, values),
            _ => throw new ValidationException(
                $"Unsupported workflow condition operator '{op}'."),
        };
    }

    /// <summary>
    /// Evaluates both operands through <see cref="EvaluateNode"/> and applies
    /// the comparison predicate to the resulting values.
    /// </summary>
    /// <exception cref="ValidationException">
    /// Thrown when either operand is a malformed condition node.
    /// </exception>
    private static bool Compare(
        JsonElement[] args,
        IReadOnlyDictionary<string, JsonElement>? values,
        Func<object?, object?, bool> predicate)
    {
        object? left = EvaluateNode(args[0], values);
        object? right = EvaluateNode(args[1], values);
        return predicate(left, right);
    }

    private static object? Coalesce(
        JsonElement[] args,
        IReadOnlyDictionary<string, JsonElement>? values)
    {
        foreach (JsonElement argument in args)
        {
            object? value = EvaluateNode(argument, values);
            if (!IsEmpty(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool IsEmpty(object? value)
    {
        return value switch
        {
            null => true,
            string text => text.Length == 0,
            System.Collections.IEnumerable items => !items.Cast<object?>().Any(),
            _ => false,
        };
    }

    private static bool ToBoolean(object? value)
    {
        return value switch
        {
            bool boolean => boolean,
            null => false,
            string text => text.Length > 0,
            double number => number != 0,
            _ => true,
        };
    }

    private static int CompareValues(object? left, object? right)
    {
        if (left is null || right is null)
        {
            if (left is null && right is null)
            {
                return 0;
            }

            return left is null ? -1 : 1;
        }

        if (left is string leftText && right is string rightText)
        {
            return string.CompareOrdinal(leftText, rightText);
        }

        if (left is bool leftBoolean && right is bool rightBoolean)
        {
            return leftBoolean.CompareTo(rightBoolean);
        }

        return ToDouble(left).CompareTo(ToDouble(right));
    }

    private static double ToDouble(object value)
    {
        return value switch
        {
            double number => number,
            float number => number,
            int number => number,
            long number => number,
            decimal number => (double)number,
            _ => Convert.ToDouble(value, CultureInfo.InvariantCulture),
        };
    }

    private static object? ToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Undefined => null,
            JsonValueKind.Object => element.GetRawText(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element
                .EnumerateArray()
                .Select(ToObject)
                .ToList(),
            _ => element.GetRawText(),
        };
    }
}
