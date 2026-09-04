using Cynara.Application.Modules.Invitations;

namespace Cynara.Api.Tests.Invitations.UnitTests;

/// <summary>
/// Unit coverage for the defensive profile-snapshot parser: it extracts
/// the actor id and capability codes that drive acceptance, and collapses
/// malformed, non-conforming, or unsupported snapshots to a closed result
/// so acceptance can fail without side effects.
/// </summary>
public sealed class InvitationProfileSnapshotParserTests
{
    [Fact]
    public void Parse_MinimalSnapshot_ExtractsActorAndCapabilities()
    {
        const string snapshot =
            /*lang=json,strict*/
            """{"actorId":"actor-invitee","capabilities":["patients.read","audit.read"]}""";

        ParsedProfileSnapshot? parsed = InvitationProfileSnapshotParser
            .TryParse(snapshot);

        Assert.NotNull(parsed);
        Assert.Equal("actor-invitee", parsed.ActorId);
        Assert.Equal(["patients.read", "audit.read"], parsed.Capabilities);
    }

    [Fact]
    public void Parse_SnapshotWithProfile_IgnoresProfileFields()
    {
        const string snapshot =
            /*lang=json,strict*/
            """
            {"actorId":"actor-a","capabilities":["tasks.read"],
             "profile":{"name":"Ada","surname":"Lovelace","phone":"+1","language":"en"}}
            """;

        ParsedProfileSnapshot? parsed = InvitationProfileSnapshotParser
            .TryParse(snapshot);

        Assert.NotNull(parsed);
        Assert.Equal("actor-a", parsed.ActorId);
        Assert.Equal(["tasks.read"], parsed.Capabilities);
    }

    [Fact]
    public void Parse_EmptyCapabilities_ReturnsValidSnapshot()
    {
        const string snapshot =
            /*lang=json,strict*/
            """{"actorId":"actor-none","capabilities":[]}""";

        ParsedProfileSnapshot? parsed = InvitationProfileSnapshotParser
            .TryParse(snapshot);

        Assert.NotNull(parsed);
        Assert.Equal("actor-none", parsed.ActorId);
        Assert.Empty(parsed.Capabilities);
    }

    [Fact]
    public void Parse_UnknownCapabilityCode_ReturnsNull()
    {
        const string snapshot =
            /*lang=json,strict*/
            """{"actorId":"actor-x","capabilities":["not-a-real-code"]}""";

        ParsedProfileSnapshot? parsed = InvitationProfileSnapshotParser
            .TryParse(snapshot);

        Assert.Null(parsed);
    }

    [Fact]
    public void Parse_MalformedJson_ReturnsNull()
    {
        const string snapshot = """{"actorId":""";

        Assert.Null(InvitationProfileSnapshotParser.TryParse(snapshot));
    }

    [Fact]
    public void Parse_NonObjectJson_ReturnsNull()
    {
        const string snapshot = """["actorId"]""";

        Assert.Null(InvitationProfileSnapshotParser.TryParse(snapshot));
    }

    [Fact]
    public void Parse_MissingActorId_ReturnsNull()
    {
        const string snapshot =
            /*lang=json,strict*/
            """{"capabilities":["patients.read"]}""";

        Assert.Null(InvitationProfileSnapshotParser.TryParse(snapshot));
    }

    [Fact]
    public void Parse_EmptyActorId_ReturnsNull()
    {
        const string snapshot =
            /*lang=json,strict*/
            """{"actorId":"","capabilities":["patients.read"]}""";

        Assert.Null(InvitationProfileSnapshotParser.TryParse(snapshot));
    }

    [Fact]
    public void Parse_ActorIdOver128Characters_ReturnsNull()
    {
        string snapshot = string.Concat(
            """{"actorId":""",
            new string('a', 129),
            "\",\"capabilities\":[\"patients.read\"]}");

        Assert.Null(InvitationProfileSnapshotParser.TryParse(snapshot));
    }

    [Fact]
    public void Parse_CapabilitiesNotAnArray_ReturnsNull()
    {
        const string snapshot =
            /*lang=json,strict*/
            """{"actorId":"actor-x","capabilities":"patients.read"}""";

        Assert.Null(InvitationProfileSnapshotParser.TryParse(snapshot));
    }

    [Fact]
    public void Parse_NonStringCapability_ReturnsNull()
    {
        const string snapshot =
            /*lang=json,strict*/
            """{"actorId":"actor-x","capabilities":[42]}""";

        Assert.Null(InvitationProfileSnapshotParser.TryParse(snapshot));
    }

    [Fact]
    public void Parse_CapabilityOver64Characters_ReturnsNull()
    {
        string snapshot = string.Concat(
            """{"actorId":"actor-x","capabilities":[""",
            new string('c', 65),
            "\"]}");

        Assert.Null(InvitationProfileSnapshotParser.TryParse(snapshot));
    }

    [Fact]
    public void Parse_BlankSnapshot_ReturnsNull()
    {
        Assert.Null(InvitationProfileSnapshotParser.TryParse(null));
        Assert.Null(InvitationProfileSnapshotParser.TryParse("   "));
    }
}