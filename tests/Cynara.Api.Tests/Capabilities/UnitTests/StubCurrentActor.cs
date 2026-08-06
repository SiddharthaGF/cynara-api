using Cynara.Application.Modules.Capabilities;

namespace Cynara.Api.Tests.Capabilities.UnitTests;

internal sealed class StubCurrentActor(string? actorId) : ICurrentActor
{
    public string? ActorId { get; } = actorId;
}
