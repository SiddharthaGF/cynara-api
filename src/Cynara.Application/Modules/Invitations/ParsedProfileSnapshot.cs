namespace Cynara.Application.Modules.Invitations;

/// <summary>
/// Defensively parsed invitation profile snapshot: the actor id and the
/// capability codes that drive credential and membership establishment,
/// plus the administrator-predefined given/family names when present. A
/// <see langword="null"/> result means the snapshot cannot be used and
/// acceptance must fail closed. Snapshot names never fail parsing on
/// their own; the workflow decides whether names are still required.
/// </summary>
public sealed record ParsedProfileSnapshot(
    string ActorId,
    IReadOnlyList<string> Capabilities,
    string? GivenName = null,
    string? FamilyName = null);
