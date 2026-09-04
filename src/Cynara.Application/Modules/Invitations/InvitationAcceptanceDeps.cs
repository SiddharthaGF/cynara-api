using Cynara.Application.Audit;
using Cynara.Application.Modules.Capabilities.Persistence;
using Cynara.Application.Modules.Hospitals;
using Cynara.Application.Modules.Invitations.Persistence;

namespace Cynara.Application.Modules.Invitations;

/// <summary>
/// Domain-track collaborators of the acceptance workflow: invitation and
/// grant persistence, lazy expiry, the identity store, and the cross-track
/// transaction coordinator.
/// </summary>
public sealed record InvitationAcceptancePersistence(
    IInvitationRepository Invitations,
    IInvitationExpiryEvaluator ExpiryEvaluator,
    IInvitationIdentityStore IdentityStore,
    IInvitationAcceptanceTransaction Transaction,
    ICapabilityAssignmentRepository Grants,
    IUnitOfWork UnitOfWork);

/// <summary>
/// Context collaborators of the acceptance workflow: snapshot validation,
/// audit staging, hospital resolution, and time.
/// </summary>
public sealed record InvitationAcceptanceContext(
    IProfileSnapshotValidator SnapshotValidator,
    IAuditWriter AuditWriter,
    IHospitalRepository Hospitals,
    IHospitalContext HospitalContext,
    TimeProvider TimeProvider);
