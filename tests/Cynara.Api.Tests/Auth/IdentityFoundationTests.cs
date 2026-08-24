using Cynara.Infrastructure;
using Cynara.Infrastructure.Modules.Identity;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Cynara.Api.Tests.Auth;

/// <summary>
/// Integration coverage for the Identity foundation added by the first
/// chained PR of the real-auth change: the dedicated
/// <see cref="CynaraIdentityDbContext"/> track with its own migration
/// history table, the additive identity migrations, the
/// <see cref="Membership"/> bridge with its unique
/// <c>(UserId, HospitalId)</c> rule, and startup migration ordering.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class IdentityFoundationTests : IDisposable
{
    public IdentityFoundationTests(PostgreSqlDatabaseFixture database)
    {
        Database = database.Settings;
        Factory = new CynaraTenantWebApplicationFactory(Database);
        Scope = Factory.Services.CreateScope();
    }

    public void Dispose()
    {
        Scope.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    private CynaraTenantWebApplicationFactory Factory { get; }

    private IServiceScope Scope { get; }

    private TestDatabaseSettings Database { get; }

    /// <summary>
    /// The test host runs InitializeDatabaseAsync at startup, so both migration
    /// tracks must already be applied to the shared database.
    /// </summary>
    [Fact]
    public async Task Startup_MigratesIdentityTrackToItsOwnHistoryTable()
    {
        CynaraIdentityDbContext identity = Scope.ServiceProvider
            .GetRequiredService<CynaraIdentityDbContext>();

        List<string> identityHistory = await identity.Database
            .SqlQueryRaw<string>(
                "SELECT \"MigrationId\" AS \"Value\" "
                + "FROM \"__CynaraIdentityMigrationsHistory\"")
            .ToListAsync();

        string[] identityMigrationIds = [.. identity.Database
            .GetService<IMigrationsAssembly>()
            .Migrations
            .Keys];

        Assert.Contains(
            identityHistory,
            migration => identityMigrationIds.Contains(migration));

        CynaraDbContext domain = Scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();

        List<string> domainHistory = await domain.Database
            .SqlQueryRaw<string>(
                "SELECT \"MigrationId\" AS \"Value\" "
                + "FROM \"__EFMigrationsHistory\"")
            .ToListAsync();

        Assert.DoesNotContain(
            domainHistory,
            migration => identityMigrationIds.Contains(migration));
    }

    [Fact]
    public async Task FreshDatabase_MigratesBothTracksWithoutCollision()
    {
        string freshDatabase = $"cynara_identity_fresh_{Guid.NewGuid():N}";
        var connectionBuilder = new NpgsqlConnectionStringBuilder(
            Database.ConnectionString)
        {
            Database = freshDatabase,
        };

        await using (NpgsqlConnection admin = new(Database.ConnectionString))
        {
            await admin.OpenAsync();
            await using NpgsqlCommand create = new(
                $"CREATE DATABASE \"{freshDatabase}\"",
                admin);
            _ = await create.ExecuteNonQueryAsync();
        }

        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddCynaraDatabase(connectionBuilder.ConnectionString);
        await using ServiceProvider provider = services.BuildServiceProvider();
        await provider.InitializeDatabaseAsync(CancellationToken.None);

        CynaraIdentityDbContext identity = provider
            .GetRequiredService<CynaraIdentityDbContext>();
        List<string> identityHistory = await identity.Database
            .SqlQueryRaw<string>(
                "SELECT \"MigrationId\" AS \"Value\" "
                + "FROM \"__CynaraIdentityMigrationsHistory\"")
            .ToListAsync();

        string[] identityMigrationIds = [.. identity.Database
            .GetService<IMigrationsAssembly>()
            .Migrations
            .Keys];

        Assert.Contains(
            identityHistory,
            migration => identityMigrationIds.Contains(migration));

        CynaraDbContext domain = provider
            .GetRequiredService<CynaraDbContext>();
        List<string> domainHistory = await domain.Database
            .SqlQueryRaw<string>(
                "SELECT \"MigrationId\" AS \"Value\" "
                + "FROM \"__EFMigrationsHistory\"")
            .ToListAsync();
        Assert.DoesNotContain(
            domainHistory,
            migration => identityMigrationIds.Contains(migration));
    }

    [Fact]
    public async Task Membership_DuplicateUserHospitalPair_IsRejected()
    {
        CynaraIdentityDbContext identity = Scope.ServiceProvider
            .GetRequiredService<CynaraIdentityDbContext>();

        var userId = Guid.NewGuid();
        var hospitalId = Guid.NewGuid();
        identity.Users.Add(new IdentityUser<Guid>
        {
            Id = userId,
            UserName = $"member-{userId}",
        });
        identity.Memberships.Add(NewMembership(userId, hospitalId, "actor-a"));
        _ = await identity.SaveChangesAsync();

        identity.Memberships.Add(NewMembership(userId, hospitalId, "actor-b"));

        DbUpdateException exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => identity.SaveChangesAsync());

        var inner = exception.InnerException as PostgresException;
        Assert.NotNull(inner);
        Assert.Equal("23505", inner.SqlState);
    }

    [Fact]
    public async Task Membership_SameUserAcrossHospitals_IsAllowed()
    {
        CynaraIdentityDbContext identity = Scope.ServiceProvider
            .GetRequiredService<CynaraIdentityDbContext>();

        var userId = Guid.NewGuid();
        var firstHospital = Guid.NewGuid();
        var secondHospital = Guid.NewGuid();
        identity.Users.Add(new IdentityUser<Guid>
        {
            Id = userId,
            UserName = $"member-{userId}",
        });
        identity.Memberships.Add(
            NewMembership(userId, firstHospital, "actor-a"));
        identity.Memberships.Add(
            NewMembership(userId, secondHospital, "actor-b"));

        _ = await identity.SaveChangesAsync();

        List<Guid> hospitals = await identity.Memberships
            .AsNoTracking()
            .Where(membership => membership.UserId == userId)
            .Select(membership => membership.HospitalId)
            .ToListAsync();

        Assert.Equal(2, hospitals.Count);
        Assert.Contains(firstHospital, hospitals);
        Assert.Contains(secondHospital, hospitals);
    }

    /// <summary>
    /// Reproduces the squash-drift failure mode: tables exist and their
    /// history table holds only pre-squash migration ids, so no stamp
    /// overlaps the current assembly. Startup must rebaseline instead of
    /// recreating existing tables, and existing data must survive.
    /// </summary>
    [Fact]
    public async Task StaleDomainMigrationHistory_IsRebaselinedWithoutRecreatingSchema()
    {
        string freshDatabase = await CreateFreshDatabaseAsync();
        var userId = Guid.NewGuid();
        string connectionString = WithDatabase(freshDatabase);

        await using (ServiceProvider first =
            await BuildInitializedProviderAsync(connectionString))
        {
            CynaraIdentityDbContext identity = first
                .GetRequiredService<CynaraIdentityDbContext>();
            identity.Users.Add(new IdentityUser<Guid>
            {
                Id = userId,
                UserName = $"stale-domain-{userId}",
            });
            _ = await identity.SaveChangesAsync();

            CynaraDbContext domain = first.GetRequiredService<CynaraDbContext>();
            _ = await domain.Database.ExecuteSqlRawAsync(
                "DELETE FROM \"__EFMigrationsHistory\"");
        }

        await using (ServiceProvider second =
            await BuildInitializedProviderAsync(connectionString))
        {
            CynaraDbContext domain = second.GetRequiredService<CynaraDbContext>();
            List<string> applied = await domain.Database
                .SqlQueryRaw<string>(
                    "SELECT \"MigrationId\" AS \"Value\" "
                    + "FROM \"__EFMigrationsHistory\"")
                .ToListAsync();

            Assert.Contains(DomainBaselineId(domain), applied);

            CynaraIdentityDbContext identity = second
                .GetRequiredService<CynaraIdentityDbContext>();
            Assert.True(await identity.Users.AnyAsync(user => user.Id == userId));
        }
    }

    [Fact]
    public async Task StaleIdentityMigrationHistory_IsRebaselinedWithoutDataLoss()
    {
        string freshDatabase = await CreateFreshDatabaseAsync();
        var userId = Guid.NewGuid();
        string connectionString = WithDatabase(freshDatabase);

        await using (ServiceProvider first =
            await BuildInitializedProviderAsync(connectionString))
        {
            CynaraIdentityDbContext identity = first
                .GetRequiredService<CynaraIdentityDbContext>();
            identity.Users.Add(new IdentityUser<Guid>
            {
                Id = userId,
                UserName = $"stale-identity-{userId}",
            });
            _ = await identity.SaveChangesAsync();
            _ = await identity.Database.ExecuteSqlRawAsync(
                "DELETE FROM \"__CynaraIdentityMigrationsHistory\"");
        }

        await using (ServiceProvider second =
            await BuildInitializedProviderAsync(connectionString))
        {
            CynaraIdentityDbContext identity = second
                .GetRequiredService<CynaraIdentityDbContext>();
            List<string> applied = await identity.Database
                .SqlQueryRaw<string>(
                    "SELECT \"MigrationId\" AS \"Value\" "
                    + "FROM \"__CynaraIdentityMigrationsHistory\"")
                .ToListAsync();

            Assert.Contains(IdentityBaselineId(identity), applied);
            Assert.True(await identity.Users.AnyAsync(user => user.Id == userId));
        }
    }

    private async Task<string> CreateFreshDatabaseAsync()
    {
        string freshDatabase = $"cynara_baseline_fresh_{Guid.NewGuid():N}";
        await using NpgsqlConnection admin = new(Database.ConnectionString);
        await admin.OpenAsync();
        await using NpgsqlCommand create = new(
            $"CREATE DATABASE \"{freshDatabase}\"",
            admin);
        _ = await create.ExecuteNonQueryAsync();
        return freshDatabase;
    }

    private string WithDatabase(string databaseName)
    {
        var connectionBuilder = new NpgsqlConnectionStringBuilder(
            Database.ConnectionString)
        {
            Database = databaseName,
        };
        return connectionBuilder.ConnectionString;
    }

    private static async Task<ServiceProvider> BuildInitializedProviderAsync(
        string connectionString)
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddCynaraDatabase(connectionString);
        ServiceProvider provider = services.BuildServiceProvider();
        await provider.InitializeDatabaseAsync(CancellationToken.None);
        return provider;
    }

    private static string DomainBaselineId(CynaraDbContext domain)
    {
        return domain.Database
            .GetService<IMigrationsAssembly>()
            .Migrations
            .Keys
            .First();
    }

    private static string IdentityBaselineId(CynaraIdentityDbContext identity)
    {
        return identity.Database
            .GetService<IMigrationsAssembly>()
            .Migrations
            .Keys
            .First();
    }

    private static Membership NewMembership(
        Guid userId,
        Guid hospitalId,
        string actorId)
    {
        return new Membership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            HospitalId = hospitalId,
            ActorId = actorId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
