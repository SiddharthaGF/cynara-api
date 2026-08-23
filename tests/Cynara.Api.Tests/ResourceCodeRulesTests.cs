using System.ComponentModel.DataAnnotations;

using Cynara.Domain.Common;

namespace Cynara.Api.Tests;

/// <summary>
/// <see cref="ResourceCodeRules.EnsureValid"/> is intentionally bounds-only:
/// regex-hostile inputs such as spaces or slashes remain valid codes.
/// </summary>
public sealed class ResourceCodeRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EnsureValid_RejectsNullOrWhitespace(string? code)
    {
        Assert.Throws<ValidationException>(
            () => ResourceCodeRules.EnsureValid(code, "Hospital"));
    }

    [Fact]
    public void EnsureValid_RejectsCodeShorterThanMinimum()
    {
        Assert.Throws<ValidationException>(
            () => ResourceCodeRules.EnsureValid(string.Empty, "Hospital"));
    }

    [Fact]
    public void EnsureValid_RejectsCodeLongerThanMaximum()
    {
        string tooLong = new('a', ResourceCodeRules.MaxLength + 1);

        Assert.Throws<ValidationException>(
            () => ResourceCodeRules.EnsureValid(tooLong, "Hospital"));
    }

    [Fact]
    public void EnsureValid_AcceptsCodeAtMinimumLength()
    {
        const string oneChar = "a";

        Exception? exception = Record.Exception(
            () => ResourceCodeRules.EnsureValid(oneChar, "Hospital"));

        Assert.Null(exception);
    }

    [Fact]
    public void EnsureValid_AcceptsCodeAtMaximumLength()
    {
        string atMax = new('a', ResourceCodeRules.MaxLength);

        Exception? exception = Record.Exception(
            () => ResourceCodeRules.EnsureValid(atMax, "Hospital"));

        Assert.Null(exception);
    }

    [Fact]
    public void EnsureValid_AcceptsCodeWithSpaces()
    {
        const string withSpace = "a b";

        Exception? exception = Record.Exception(
            () => ResourceCodeRules.EnsureValid(withSpace, "Hospital"));

        Assert.Null(exception);
    }

    [Fact]
    public void EnsureValid_AcceptsCodeWithSpecialCharacters()
    {
        const string withSlash = "a/b";

        Exception? exception = Record.Exception(
            () => ResourceCodeRules.EnsureValid(withSlash, "Hospital"));

        Assert.Null(exception);
    }

    [Fact]
    public void EnsureValid_ErrorMessageMentionsEntityName()
    {
        ValidationException exception = Assert.Throws<ValidationException>(
            () => ResourceCodeRules.EnsureValid("   ", "Discipline"));

        Assert.Contains("Discipline", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constants_AreInternallyConsistent()
    {
        Assert.True(ResourceCodeRules.MinLength <= ResourceCodeRules.MaxLength);
        Assert.False(string.IsNullOrEmpty(ResourceCodeRules.Pattern));
    }
}
