using System.Text.Json;

using Cynara.Application.Modules.Audit.Persistence;
using Cynara.Application.Modules.Hospitals;
using Cynara.Domain.Audit;

namespace Cynara.Application.Audit;

public sealed class AuditWriter(
    IAuditRepository audit,
    IHospitalContext hospitalContext) : IAuditWriter
{
    public void Append(
        string resourceType,
        Guid resourceId,
        string action,
        string? actorId,
        DateTimeOffset occurredAt,
        object metadata,
        Guid? patientId = null,
        Guid? encounterId = null,
        Guid? workflowDefinitionId = null)
    {
        hospitalContext.RequireResolved();
        audit.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalContext.HospitalId,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Action = action,
            ActorId = actorId,
            OccurredAt = occurredAt,
            PatientId = patientId,
            EncounterId = encounterId,
            WorkflowDefinitionId = workflowDefinitionId,
            MetadataJson = JsonSerializer.Serialize(
                metadata,
                CanonicalJsonOptions.Instance),
        });
    }
}
