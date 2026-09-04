namespace Cynara.Application.Modules.Invitations;

/// <summary>
/// Defensively parsed invitation profile snapshot: the actor id and the
/// capability codes that drive credential and membership establishment.
/// A <see langword="null"/> result means the snapshot cannot be used and
/// acceptance must fail closed.
/// </summary>
public sealed record ParsedProfileSnapshot(
    string ActorId,
    IReadOnlyList<string> Capabilities);
