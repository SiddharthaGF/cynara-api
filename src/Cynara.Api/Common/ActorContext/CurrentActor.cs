using Cynara.Application.Modules.Capabilities;

namespace Cynara.Api.Common.ActorContext;

/// <summary>
/// Header-backed current-actor implementation used by the integration test
/// seam. The <c>X-Actor-Id</c> header is the actor identity for both audit
/// attribution and capability resolution; a missing header yields
/// <see langword="null"/>, which resolves to an empty capability set (deny
/// by default). Out-of-request flows (such as startup seeding) fall back to
/// the scoped <see cref="CurrentActorOverride"/>. Production registers
/// <see cref="PrincipalCurrentActor"/> instead; this type is deliberately
/// not wired in the production composition root.
/// </summary>
public sealed class CurrentActor(
    IHttpContextAccessor httpContextAccessor,
    CurrentActorOverride actorOverride) : ICurrentActor
{
    public string? ActorId => httpContextAccessor.HttpContext?.GetActorIdFromHeader()
        ?? actorOverride.ActorId;
}
