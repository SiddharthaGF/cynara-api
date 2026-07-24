using Cynara.Domain.Audit;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cynara.Infrastructure.Modules.Audit;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("audit_events");
        _ = builder.HasKey(item => item.Id);
        _ = builder.Property(item => item.HospitalId).IsRequired();
        _ = builder.Property(item => item.ResourceType).HasMaxLength(64).IsRequired();
        _ = builder.Property(item => item.Action).HasMaxLength(64).IsRequired();
        _ = builder.Property(item => item.ActorId).HasMaxLength(128);
        _ = builder.HasIndex(item => item.HospitalId);
        _ = builder.HasIndex(item => new { item.HospitalId, item.ResourceType, item.ResourceId });
        _ = builder.HasIndex(item => new { item.HospitalId, item.ActorId });
        _ = builder.HasIndex(item => new { item.HospitalId, item.OccurredAt });
    }
}
