using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace Cynara.Infrastructure.Modules.Identity;

/// <summary>
/// Dedicated persistence context for ASP.NET Core Identity, OpenIddict,
/// and the user-to-actor <see cref="Membership"/> bridge, kept separate
/// from the domain unit of work and given its own migrations history table.
/// </summary>
public sealed class CynaraIdentityDbContext(
    DbContextOptions<CynaraIdentityDbContext> options)
    : IdentityDbContext<CynaraUser, IdentityRole<Guid>, Guid>(options),
        IDataProtectionKeyContext
{
    /// <summary>
    /// EF migrations history table for the identity track. Distinct from
    /// the domain track's <c>__EFMigrationsHistory</c> table so both tracks
    /// can be migrated independently against the same database.
    /// </summary>
    public const string MigrationsHistoryTableName =
        "__CynaraIdentityMigrationsHistory";

    public DbSet<Membership> Memberships => Set<Membership>();

    /// <summary>
    /// DataProtection key ring storage; persisting keys here keeps refresh
    /// tokens valid across restarts, deploys, and scaled instances.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.OnModelCreating(builder);
        _ = builder.UseOpenIddict();
        _ = builder.ApplyConfiguration(new CynaraUserEntityConfiguration());
        _ = builder.ApplyConfiguration(new MembershipEntityConfiguration());
        _ = builder.ApplyConfiguration(new DataProtectionKeyEntityConfiguration());
    }
}
