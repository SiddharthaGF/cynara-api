namespace Cynara.IdentitySpike.Domain;

/// <summary>
/// Spike-only bridge between an Identity user, a hospital workspace, and the
/// Cynara domain actor identity. A user may hold several memberships (one per
/// hospital); each membership pins the actor identity used inside that
/// hospital for capability resolution and audit attribution.
/// </summary>
public sealed class Membership
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Identity user owning this membership.</summary>
    public Guid UserId { get; set; }

    /// <summary>Hospital workspace the membership grants access to.</summary>
    public Guid HospitalId { get; set; }

    /// <summary>
    /// Cynara domain actor identity for this user inside the hospital. This
    /// is the value exposed through <c>ICurrentActor.ActorId</c> and used to
    /// resolve capability assignments.
    /// </summary>
    public required string ActorId { get; set; }

    /// <summary>UTC timestamp when the membership was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
