using Cynara.Application.Modules.Documents;
using Cynara.Domain.Documents;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cynara.Infrastructure.Modules.Documents;

/// <summary>
/// EF entity configuration for the <see cref="ClinicalDocument"/> aggregate.
/// Indexes support hospital-scoped list filters by encounter, patient,
/// catalog entry, and status.
/// </summary>
public sealed class ClinicalDocumentEntityConfiguration
    : IEntityTypeConfiguration<ClinicalDocument>
{
    public void Configure(EntityTypeBuilder<ClinicalDocument> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("clinical_documents");
        _ = builder.HasKey(item => item.Id);
        _ = builder.Property(item => item.HospitalId).IsRequired();
        _ = builder.HasIndex(item => item.HospitalId);
        _ = builder.HasIndex(item => new { item.HospitalId, item.EncounterId });
        _ = builder.HasIndex(item => new { item.HospitalId, item.PatientId });
        _ = builder.HasIndex(item => new
        {
            item.HospitalId,
            item.DocumentDefinitionId,
            item.EncounterId,
        });
        _ = builder.HasIndex(item => new { item.HospitalId, item.Status });
        _ = builder.Property(item => item.DocumentDefinitionId).IsRequired();
        _ = builder.Property(item => item.PatientId).IsRequired();
        _ = builder.Property(item => item.EncounterId).IsRequired();
        _ = builder.Property(item => item.FormVersionId).IsRequired();
        _ = builder.Property(item => item.FormResponseId).IsRequired();
        _ = builder.Property(item => item.AuthorId)
            .HasMaxLength(ClinicalDocumentFieldLimits.AuthorIdMaxLength);
        _ = builder.Property(item => item.Status)
            .HasConversion<string>()
            .HasMaxLength(32);
        _ = builder.Property(item => item.RowVersion).IsConcurrencyToken();
        _ = builder.HasQueryFilter(_ => true);
    }
}
