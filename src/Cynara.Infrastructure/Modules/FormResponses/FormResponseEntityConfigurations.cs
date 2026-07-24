using Cynara.Domain.Forms;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cynara.Infrastructure.Modules.FormResponses;

public sealed class FormResponseConfiguration
    : IEntityTypeConfiguration<FormResponse>
{
    public void Configure(EntityTypeBuilder<FormResponse> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("form_responses");
        _ = builder.HasKey(item => item.Id);
        _ = builder.Property(item => item.HospitalId).IsRequired();
        _ = builder.HasIndex(item => item.HospitalId);
        _ = builder.Property(item => item.AnswersJson).IsRequired();
        _ = builder.Property(item => item.RowVersion).IsConcurrencyToken();
        _ = builder.HasOne(item => item.FormVersion)
            .WithMany()
            .HasForeignKey(item => item.FormVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        _ = builder.HasQueryFilter(item =>
            item.HospitalId == item.FormVersion.HospitalId
            && item.DeletedAt == null);
    }
}

public sealed class FormResponseRevisionConfiguration
    : IEntityTypeConfiguration<FormResponseRevision>
{
    public void Configure(EntityTypeBuilder<FormResponseRevision> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("form_response_revisions");
        _ = builder.HasKey(item => item.Id);
        _ = builder.Property(item => item.HospitalId).IsRequired();
        _ = builder.HasIndex(item => item.HospitalId);
        _ = builder.Property(item => item.AnswersJson).IsRequired();
        _ = builder.Property(item => item.ActorId).HasMaxLength(128);
        _ = builder.HasIndex(item => new
        {
            item.FormResponseId,
            item.RevisionNumber,
        }).IsUnique();
        _ = builder.HasOne(item => item.FormResponse)
            .WithMany(item => item.Revisions)
            .HasForeignKey(item => item.FormResponseId)
            .OnDelete(DeleteBehavior.Cascade);
        _ = builder.HasQueryFilter(item =>
            item.HospitalId == item.FormResponse.HospitalId
            && item.FormResponse.DeletedAt == null);
    }
}
