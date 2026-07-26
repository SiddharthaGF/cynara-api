using System.ComponentModel.DataAnnotations;

using Cynara.Domain.Common;

namespace Cynara.Api.Tests;

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

        ResourceCodeRules.EnsureValid(oneChar, "Hospital");
    }

    [Fact]
    public void EnsureValid_AcceptsCodeAtMaximumLength()
    {
        string atMax = new('a', ResourceCodeRules.MaxLength);

        ResourceCodeRules.EnsureValid(atMax, "Hospital");
    }

    [Fact]
    public void EnsureValid_AcceptsCodeWithSpaces()
    {
        // The regex would reject "a b" because of the space, but
        // ResourceCodeRules.EnsureValid is intentionally bounds-only.
        const string withSpace = "a b";

        ResourceCodeRules.EnsureValid(withSpace, "Hospital");
    }

    [Fact]
    public void EnsureValid_AcceptsCodeWithSpecialCharacters()
    {
        // The regex would reject "a/b" because of the slash, but
        // ResourceCodeRules.EnsureValid is intentionally bounds-only.
        const string withSlash = "a/b";

        ResourceCodeRules.EnsureValid(withSlash, "Hospital");
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
