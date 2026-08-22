using Cynara.Domain.Capabilities;

namespace Cynara.Api.Tests.Capabilities.UnitTests;

/// <summary>
/// Catalog invariants for capability codes. Scope breadth lives exclusively
/// on the grant row's scope dimension, so the catalog must never grow
/// scope-encoded variant codes: every entry keeps its single
/// <c>&lt;area&gt;.&lt;verb&gt;</c> shape and the users-area read surface is
/// one code serving both grant scopes.
/// </summary>
public sealed class CapabilityCatalogTests
{
    [Fact]
    public void Catalog_ExposesNoScopeEncodedVariantCodes()
    {
        Assert.NotEmpty(CapabilityCodes.All);
        foreach (string code in CapabilityCodes.All)
        {
            foreach (string suffix in new[] { ".global", ".platform", ".hospital" })
            {
                Assert.False(
                    code.EndsWith(suffix, StringComparison.Ordinal),
                    $"Capability code '{code}' must not carry the "
                    + $"scope-encoded suffix '{suffix}'.");
            }

            Assert.Equal(2, code.Split('.').Length);
        }
    }

    [Fact]
    public void UsersRead_IsSingleCatalogCode_ServingBothScopes()
    {
        Assert.Equal("users.read", CapabilityCodes.UsersRead);
        Assert.Equal(
            1,
            CapabilityCodes.All.Count(code => string.Equals(
                code,
                CapabilityCodes.UsersRead,
                StringComparison.Ordinal)));
    }
}
