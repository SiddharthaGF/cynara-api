using System.Security.Cryptography;
using System.Text;

using Cynara.Application.Modules.Invitations;

namespace Cynara.Api.Tests.Invitations.UnitTests;

/// <summary>
/// Unit coverage for invitation token hashing: canonical uppercase SHA-256
/// hex digests so persisted hashes are exact-match lookup keys.
/// </summary>
public sealed class InvitationTokenHasherUnitTests
{
    /// <summary>Pre-computed SHA-256 of ASCII "abc".</summary>
    [Fact]
    public void Hash_KnownVector_ProducesCanonicalUppercaseSha256Hex()
    {
        string expected =
            "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";

        string actual = InvitationTokenHasher.Hash("abc");

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Hash_MatchesFrameworkReferenceImplementation()
    {
        const string token = "cynara-invite-link-token-2026";
        string expected = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        string actual = InvitationTokenHasher.Hash(token);

        Assert.Equal(expected, actual);
        Assert.Equal(64, actual.Length);
    }

    [Fact]
    public void Hash_IsDeterministic_ForRepeatedCalls()
    {
        const string token = "single-use-72h-token";

        Assert.Equal(
            InvitationTokenHasher.Hash(token),
            InvitationTokenHasher.Hash(token));
    }

    [Fact]
    public void Hash_DifferentTokens_ProduceDifferentHashes()
    {
        string first = InvitationTokenHasher.Hash("token-one");
        string second = InvitationTokenHasher.Hash("token-two");

        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// ThrowIfNullOrWhiteSpace raises ArgumentNullException for null and
    /// ArgumentException for blank strings; both derive from the base.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Hash_NullOrWhitespaceToken_Throws(string? token)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => InvitationTokenHasher.Hash(token!));
    }
}
