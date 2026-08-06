using Cynara.Application.Modules.Capabilities;

namespace Cynara.Api.Common.ActorContext;

/// <summary>
/// Header-backed current-actor implementation. The <c>X-Actor-Id</c> header
/// is the actor identity for both audit attribution and capability
/// resolution; a missing header yields <see langword="null"/>, which resolves
/// to an empty capability set (deny by default). Out-of-request flows (such
/// as startup seeding) fall back to the scoped
/// <see cref="CurrentActorOverride"/>.
/// </summary>
public sealed class CurrentActor(
    IHttpContextAccessor httpContextAccessor,
    CurrentActorOverride actorOverride) : ICurrentActor
{
    public string? ActorId => httpContextAccessor.HttpContext?.GetActorId()
        ?? actorOverride.ActorId;
}
