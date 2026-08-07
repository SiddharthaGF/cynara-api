using System.Text.Json;

using Cynara.Application;
using Cynara.Application.Modules.Workflows;

namespace Cynara.Api.Tests.Workflows;

/// <summary>
/// Unit tests for the server-side workflow transition condition evaluator.
/// Conditions are restricted declarative expressions (ref, lit, comparison,
/// boolean, utility); only boolean results are meaningful for guards.
/// </summary>
public sealed class WorkflowConditionEvaluatorTests
{
    [Theory]
    [InlineData("3", true)]
    [InlineData("5", true)]
    [InlineData("6", false)]
    [InlineData("10", false)]
    public void Lte_NumberComparison_EvaluatesBoundary(string score, bool expected)
    {
        bool result = Evaluate(
            Condition(
                /*lang=json,strict*/ """
                { "op": "lte", "args": [ { "ref": "triage-score" }, { "lit": 5 } ] }
                """),
            Values("triage-score", Number(score)));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Eq_String_True()
    {
        bool result = Evaluate(
            Condition(
                /*lang=json,strict*/ """
                { "op": "eq", "args": [ { "ref": "disposition" }, { "lit": "admit" } ] }
                """),
            Values("disposition", String("admit")));

        Assert.True(result);
    }

    [Fact]
    public void Neq_String_False()
    {
        bool result = Evaluate(
            Condition(
                /*lang=json,strict*/ """
                { "op": "neq", "args": [ { "ref": "disposition" }, { "lit": "admit" } ] }
                """),
            Values("disposition", String("admit")));

        Assert.False(result);
    }

    [Theory]
    [InlineData("8", false)]
    [InlineData("9", false)]
    [InlineData("10", false)]
    [InlineData("11", true)]
    public void Gt_NumberComparison_EvaluatesBoundary(string score, bool expected)
    {
        bool result = Evaluate(
            Condition(
                /*lang=json,strict*/ """
                { "op": "gt", "args": [ { "ref": "triage-score" }, { "lit": 10 } ] }
                """),
            Values("triage-score", Number(score)));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void And_AllMustBeTrue()
    {
        bool result = Evaluate(
            Condition(
                /*lang=json,strict*/ """
                {
                  "op": "and",
                  "args": [
                    { "op": "eq", "args": [ { "ref": "admitted" }, { "lit": true } ] },
                    { "op": "gte", "args": [ { "ref": "triage-score" }, { "lit": 5 } ] }
                  ]
                }
                """),
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["admitted"] = True(),
                ["triage-score"] = Number(7),
            });

        Assert.True(result);
    }

    [Fact]
    public void And_OneFalse_IsFalse()
    {
        bool result = Evaluate(
            Condition(
                /*lang=json,strict*/ """
                {
                  "op": "and",
                  "args": [
                    { "op": "eq", "args": [ { "ref": "admitted" }, { "lit": true } ] },
                    { "op": "gte", "args": [ { "ref": "triage-score" }, { "lit": 5 } ] }
                  ]
                }
                """),
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["admitted"] = True(),
                ["triage-score"] = Number(3),
            });

        Assert.False(result);
    }

    [Fact]
    public void Or_AnyTrue_IsTrue()
    {
        bool result = Evaluate(
            Condition(
                /*lang=json,strict*/ """
                {
                  "op": "or",
                  "args": [
                    { "op": "eq", "args": [ { "ref": "admitted" }, { "lit": true } ] },
                    { "op": "eq", "args": [ { "ref": "flag" }, { "lit": "escalate" } ] }
                  ]
                }
                """),
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["admitted"] = False(),
                ["flag"] = String("escalate"),
            });

        Assert.True(result);
    }

    [Fact]
    public void Not_InvertsBoolean()
    {
        bool result = Evaluate(
            Condition(
                /*lang=json,strict*/ """
                { "op": "not", "args": [ { "op": "eq", "args": [ { "ref": "admitted" }, { "lit": true } ] } ] }
                """),
            Values("admitted", False()));

        Assert.True(result);
    }

    [Fact]
    public void Empty_True_WhenMissingValue()
    {
        bool result = Evaluate(
            Condition(
                /*lang=json,strict*/ """
                { "op": "empty", "args": [ { "ref": "note" } ] }
                """),
            new Dictionary<string, JsonElement>(StringComparer.Ordinal));

        Assert.True(result);
    }

    [Fact]
    public void Empty_False_WhenValuePresent()
    {
        bool result = Evaluate(
            Condition(
                /*lang=json,strict*/ """
                { "op": "empty", "args": [ { "ref": "note" } ] }
                """),
            Values("note", String("triage complete")));

        Assert.False(result);
    }

    [Fact]
    public void Coalesce_PicksFirstNonNull()
    {
        bool result = Evaluate(
            Condition(
                /*lang=json,strict*/ """
                { "op": "eq", "args": [ { "op": "coalesce", "args": [ { "ref": "missing" }, { "ref": "fallback" } ] }, { "lit": "second" } ] }
                """),
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["fallback"] = String("second"),
            });

        Assert.True(result);
    }

    [Fact]
    public void MissingRef_ComparesAsNull()
    {
        bool result = Evaluate(
            Condition(
                /*lang=json,strict*/ """
                { "op": "eq", "args": [ { "ref": "never-supplied" }, { "lit": null } ] }
                """),
            new Dictionary<string, JsonElement>(StringComparer.Ordinal));

        Assert.True(result);
    }

    [Fact]
    public void UnknownOperator_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(
            () => Evaluate(
                Condition(
                    /*lang=json,strict*/ """
                    { "op": "matches", "args": [ { "ref": "x" }, { "lit": "y" } ] }
                    """),
                Values("x", String("y"))));
    }

    [Fact]
    public void LiteralOnly_ReturnsItsTruthiness()
    {
        Assert.True(
            Evaluate(
                Condition(/*lang=json,strict*/ """
                          { "lit": true }
                          """),
                values: null));
        Assert.False(
            Evaluate(
                Condition(/*lang=json,strict*/ """
                          { "lit": false }
                          """),
                values: null));
        Assert.False(
            Evaluate(
                Condition(/*lang=json,strict*/ """
                          { "lit": null }
                          """),
                values: null));
    }

    private static bool Evaluate(
        JsonElement condition,
        IReadOnlyDictionary<string, JsonElement>? values)
    {
        return WorkflowConditionEvaluator.Evaluate(condition, values);
    }

    private static JsonElement Condition(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static Dictionary<string, JsonElement> Values(
        string name,
        JsonElement value)
    {
        return new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [name] = value,
        };
    }

    private static JsonElement Number(string raw)
    {
        return JsonDocument.Parse(raw).RootElement.Clone();
    }

    private static JsonElement Number(int value)
    {
        return JsonSerializer.SerializeToElement(value);
    }

    private static JsonElement String(string raw)
    {
        return JsonSerializer.SerializeToElement(raw);
    }

    private static JsonElement True()
    {
        return JsonDocument.Parse("true").RootElement.Clone();
    }

    private static JsonElement False()
    {
        return JsonDocument.Parse("false").RootElement.Clone();
    }
}
