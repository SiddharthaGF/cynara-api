using Cynara.Application.Modules.Patients;
using Cynara.Domain.Patients;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cynara.Infrastructure.Modules.Patients;

/// <summary>
/// EF entity configuration for the <see cref="Patient"/> aggregate; the
/// composite MRN index enforces hospital-scoped uniqueness (CYN-49) and
/// the name/national-id indexes support the search endpoint.
/// </summary>
public sealed class PatientEntityConfiguration
    : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("patients");
        _ = builder.HasKey(item => item.Id);
        _ = builder.Property(item => item.HospitalId).IsRequired();
        _ = builder.HasIndex(item => item.HospitalId);
        _ = builder.HasIndex(item => new { item.HospitalId, item.NormalizedMrn })
            .IsUnique();
        _ = builder.HasIndex(item =>
            new { item.HospitalId, item.NormalizedNationalId });
        _ = builder.HasIndex(item => new
        {
            item.HospitalId,
            item.NormalizedFamilyName,
            item.NormalizedGivenName,
        });
        _ = builder.Property(item => item.Mrn)
            .HasMaxLength(PatientFieldLimits.MrnMaxLength)
            .IsRequired();
        _ = builder.Property(item => item.NormalizedMrn)
            .HasMaxLength(PatientFieldLimits.MrnMaxLength)
            .IsRequired();
        _ = builder.Property(item => item.NationalId)
            .HasMaxLength(PatientFieldLimits.NationalIdMaxLength);
        _ = builder.Property(item => item.NormalizedNationalId)
            .HasMaxLength(PatientFieldLimits.NationalIdMaxLength);
        _ = builder.Property(item => item.GivenName)
            .HasMaxLength(PatientFieldLimits.NameMaxLength)
            .IsRequired();
        _ = builder.Property(item => item.NormalizedGivenName)
            .HasMaxLength(PatientFieldLimits.NameMaxLength)
            .IsRequired();
        _ = builder.Property(item => item.FamilyName)
            .HasMaxLength(PatientFieldLimits.NameMaxLength)
            .IsRequired();
        _ = builder.Property(item => item.NormalizedFamilyName)
            .HasMaxLength(PatientFieldLimits.NameMaxLength)
            .IsRequired();
        _ = builder.Property(item => item.Sex)
            .HasConversion<string>()
            .HasMaxLength(16);
        _ = builder.Property(item => item.BloodType)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        _ = builder.Property(item => item.Status)
            .HasConversion<string>()
            .HasMaxLength(32);
        _ = builder.Property(item => item.RowVersion).IsConcurrencyToken();
        _ = builder.HasQueryFilter(_ => true);
    }
}
