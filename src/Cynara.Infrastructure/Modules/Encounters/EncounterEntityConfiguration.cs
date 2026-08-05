using Cynara.Application.Modules.Encounters;
using Cynara.Domain.Encounters;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cynara.Infrastructure.Modules.Encounters;

/// <summary>
/// EF entity configuration for the <see cref="Encounter"/> aggregate.
/// Indexes support hospital-scoped list filters by patient, facility,
/// clinical area, and status.
/// </summary>
public sealed class EncounterEntityConfiguration
    : IEntityTypeConfiguration<Encounter>
{
    public void Configure(EntityTypeBuilder<Encounter> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("encounters");
        _ = builder.HasKey(item => item.Id);
        _ = builder.Property(item => item.HospitalId).IsRequired();
        _ = builder.HasIndex(item => item.HospitalId);
        _ = builder.HasIndex(item => new { item.HospitalId, item.PatientId });
        _ = builder.HasIndex(item => new { item.HospitalId, item.FacilityId });
        _ = builder.HasIndex(item => new { item.HospitalId, item.ClinicalAreaId });
        _ = builder.HasIndex(item => new { item.HospitalId, item.Status });
        _ = builder.Property(item => item.PatientId).IsRequired();
        _ = builder.Property(item => item.FacilityId).IsRequired();
        _ = builder.Property(item => item.ClinicalAreaId).IsRequired();
        _ = builder.Property(item => item.Type)
            .HasConversion<string>()
            .HasMaxLength(32);
        _ = builder.Property(item => item.ResponsibleProfessionalId)
            .HasMaxLength(EncounterFieldLimits.ResponsibleProfessionalIdMaxLength)
            .IsRequired();
        _ = builder.Property(item => item.Status)
            .HasConversion<string>()
            .HasMaxLength(32);
        _ = builder.Property(item => item.RowVersion).IsConcurrencyToken();
        _ = builder.HasQueryFilter(_ => true);
    }
}
