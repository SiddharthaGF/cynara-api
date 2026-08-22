using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Cynara.Infrastructure.Modules.Identity;

/// <summary>
/// Dedicated persistence context for ASP.NET Core Identity, OpenIddict,
/// and the user-to-actor <see cref="Membership"/> bridge. Kept separate
/// from the domain <c>CynaraDbContext</c> so authentication state never
/// couples to the domain unit of work, and given its own EF migrations
/// history table so the identity track cannot collide with the domain
/// migration track.
/// </summary>
public sealed class CynaraIdentityDbContext(
    DbContextOptions<CynaraIdentityDbContext> options)
    : IdentityDbContext<IdentityUser<Guid>, IdentityRole<Guid>, Guid>(options)
{
    /// <summary>
    /// EF migrations history table for the identity track. Distinct from
    /// the domain track's <c>__EFMigrationsHistory</c> table so both tracks
    /// can be migrated independently against the same database.
    /// </summary>
    public const string MigrationsHistoryTableName =
        "__CynaraIdentityMigrationsHistory";

    public DbSet<Membership> Memberships => Set<Membership>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.OnModelCreating(builder);
        _ = builder.UseOpenIddict();
        _ = builder.ApplyConfiguration(new MembershipEntityConfiguration());
    }
}
