using Cynara.Domain.Common;
using Cynara.Domain.Documents;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cynara.Infrastructure.Modules.Documents;

/// <summary>
/// EF entity configuration for the <see cref="DocumentDefinition"/>
/// aggregate; codes are unique per hospital and the FormVersionId snapshot
/// is restrict-deleted so historical documents stay resolvable.
/// </summary>
public sealed class DocumentDefinitionConfiguration
    : IEntityTypeConfiguration<DocumentDefinition>
{
    public void Configure(EntityTypeBuilder<DocumentDefinition> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("document_definitions");
        _ = builder.HasKey(item => item.Id);
        _ = builder.Property(item => item.HospitalId).IsRequired();
        _ = builder.HasIndex(item => item.HospitalId);
        _ = builder.HasIndex(item => new { item.HospitalId, item.Code }).IsUnique();
        _ = builder.Property(item => item.Code)
            .HasMaxLength(ResourceCodeRules.MaxLength)
            .IsRequired();
        _ = builder.Property(item => item.Name).HasMaxLength(256).IsRequired();
        _ = builder.Property(item => item.Status)
            .HasConversion<string>()
            .HasMaxLength(32);
        _ = builder.Property(item => item.RowVersion).IsConcurrencyToken();

        _ = builder.HasOne(item => item.FormDefinition)
            .WithMany()
            .HasForeignKey(item => item.FormDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
        _ = builder.HasOne(item => item.FormVersion)
            .WithMany()
            .HasForeignKey(item => item.FormVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        _ = builder.HasOne(item => item.Facility)
            .WithMany()
            .HasForeignKey(item => item.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);
        _ = builder.HasOne(item => item.ClinicalArea)
            .WithMany()
            .HasForeignKey(item => item.ClinicalAreaId)
            .OnDelete(DeleteBehavior.Restrict);
        _ = builder.HasOne(item => item.Discipline)
            .WithMany()
            .HasForeignKey(item => item.DisciplineId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.HasIndex(item => item.FormVersionId);
        _ = builder.HasIndex(item => new
        {
            item.HospitalId,
            item.FacilityId,
            item.ClinicalAreaId,
            item.DisciplineId,
        });
        _ = builder.HasQueryFilter(_ => true);
    }
}
