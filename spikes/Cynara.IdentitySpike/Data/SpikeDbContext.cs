using Cynara.Domain.Capabilities;
using Cynara.Domain.Common;
using Cynara.Domain.Hospitals;
using Cynara.IdentitySpike.Domain;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Cynara.IdentitySpike.Data;

/// <summary>
/// SQLite-backed spike database. Holds the Identity stores, the OpenIddict
/// stores, and the Cynara domain tables the spike reuses (hospitals,
/// capability assignments) plus the spike-only membership table. The database
/// is disposable: it is deleted and recreated on every startup.
/// </summary>
public sealed class SpikeDbContext :
    IdentityDbContext<IdentityUser<Guid>, IdentityRole<Guid>, Guid>
{
    /// <summary>Creates a spike database context.</summary>
    public SpikeDbContext(DbContextOptions<SpikeDbContext> options)
        : base(options)
    {
    }

    /// <summary>Reused Cynara hospital workspace rows.</summary>
    public DbSet<Hospital> Hospitals => Set<Hospital>();

    /// <summary>Reused Cynara capability assignments.</summary>
    public DbSet<CapabilityAssignment> CapabilityAssignments =>
        Set<CapabilityAssignment>();

    /// <summary>Spike-only user-to-hospital memberships.</summary>
    public DbSet<Membership> Memberships => Set<Membership>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.OnModelCreating(builder);

        _ = builder.Entity<Hospital>(entity =>
        {
            _ = entity.ToTable("hospitals");
            _ = entity.HasKey(item => item.Id);
            _ = entity.HasIndex(item => item.Code).IsUnique();
            _ = entity.Property(item => item.Code)
                .HasMaxLength(ResourceCodeRules.MaxLength)
                .IsRequired();
            _ = entity.Property(item => item.Name)
                .HasMaxLength(256)
                .IsRequired();
            _ = entity.Property(item => item.Status)
                .HasConversion<string>()
                .HasMaxLength(32);
            _ = entity.Property(item => item.RowVersion)
                .IsConcurrencyToken();
        });

        _ = builder.Entity<CapabilityAssignment>(entity =>
        {
            _ = entity.ToTable("capability_assignments");
            _ = entity.HasKey(item => item.Id);
            _ = entity.Property(item => item.HospitalId).IsRequired();
            _ = entity.HasIndex(item => item.HospitalId);
            _ = entity.HasIndex(item => new
            {
                item.HospitalId,
                item.ActorId,
                item.Capability,
            }).IsUnique();
            _ = entity.Property(item => item.ActorId)
                .HasMaxLength(128)
                .IsRequired();
            _ = entity.Property(item => item.Capability)
                .HasMaxLength(64)
                .IsRequired();
            _ = entity.Property(item => item.AssignedBy)
                .HasMaxLength(128);
            _ = entity.Property(item => item.RowVersion)
                .IsConcurrencyToken();
        });

        _ = builder.Entity<Membership>(entity =>
        {
            _ = entity.ToTable("memberships");
            _ = entity.HasKey(item => item.Id);
            _ = entity.Property(item => item.UserId).IsRequired();
            _ = entity.Property(item => item.HospitalId).IsRequired();
            _ = entity.Property(item => item.ActorId)
                .HasMaxLength(128)
                .IsRequired();
            _ = entity.HasIndex(item => new
            {
                item.UserId,
                item.HospitalId,
            }).IsUnique();
            _ = entity.HasIndex(item => item.HospitalId);
        });
    }
}
