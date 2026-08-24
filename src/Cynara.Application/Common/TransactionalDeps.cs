using Cynara.Application.Audit;
using Cynara.Application.Modules.Capabilities;

namespace Cynara.Application.Common;

/// <summary>
/// Cross-cutting collaborators shared by transactional workflow services:
/// atomic persistence, audit staging and capability enforcement.
/// </summary>
public sealed record TransactionalDeps(
    IUnitOfWork UnitOfWork,
    IAuditWriter AuditWriter,
    ICapabilityGuard CapabilityGuard);
