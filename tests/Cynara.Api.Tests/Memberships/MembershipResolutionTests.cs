using Cynara.Domain.Memberships;
using Cynara.Infrastructure.Modules.Identity;

using Microsoft.AspNetCore.Identity;

using Npgsql;

namespace Cynara.Api.Tests.Memberships;

/// <summary>
/// DB-level slice-1 schema tests: the filtered unique indexes admit exactly
/// one Active row per (user, hospital) and per (hospital, actor), while
/// Revoked history rows stay outside the uniqueness window so a revoked pair
/// or actor id can be re-established. Exercised against the real
/// <c>AddMembershipLifecycle</c> migration on the Testcontainers Postgres.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class MembershipResolutionTests : IDisposable
{
    public MembershipResolutionTests(PostgreSqlDatabaseFixture database)
    {
        Database = database.Settings;
        Factory = new CynaraWebApplicationFactory(Database);
    }

    public void Dispose()
    {
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    private CynaraWebApplicationFactory Factory { get; }

    private TestDatabaseSettings Database { get; }

    [Fact]
    public async Task DuplicateActiveMembership_SameUserAndHospital_IsRejected()
    {
        await Factory.ResetDatabaseAsync();
        await using AsyncServiceScope scope = Factory.Services
            .CreateAsyncScope();
        CynaraIdentityDbContext identity = scope.ServiceProvider
            .GetRequiredService<CynaraIdentityDbContext>();
        IdentityUser<Guid> user = await CreateUserAsync(
            identity,
            "dup-pair@cynara.dev");
        var hospitalId = Guid.NewGuid();

        await InsertMembershipAsync(
            identity,
            user.Id,
            hospitalId,
            "doctor-dup",
            MembershipStatus.Active);

        PostgresException? violation = await TryInsertMembershipAsync(
            identity,
            user.Id,
            hospitalId,
            "doctor-other",
            MembershipStatus.Active);

        Assert.NotNull(violation);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, violation.SqlState);
    }

    [Fact]
    public async Task ActiveAndRevokedMemberships_SamePair_AreBothAdmitted()
    {
        await Factory.ResetDatabaseAsync();
        await using AsyncServiceScope scope = Factory.Services
            .CreateAsyncScope();
        CynaraIdentityDbContext identity = scope.ServiceProvider
            .GetRequiredService<CynaraIdentityDbContext>();
        IdentityUser<Guid> user = await CreateUserAsync(
            identity,
            "pair-history@cynara.dev");
        var hospitalId = Guid.NewGuid();

        await InsertMembershipAsync(
            identity,
            user.Id,
            hospitalId,
            "doctor-history",
            MembershipStatus.Active);
        await InsertMembershipAsync(
            identity,
            user.Id,
            hospitalId,
            "doctor-history",
            MembershipStatus.Revoked);

        Assert.Equal(
            2,
            await identity.Memberships.CountAsync(
                item => item.UserId == user.Id));
    }

    [Fact]
    public async Task DuplicateActiveActor_SameHospital_IsRejected()
    {
        await Factory.ResetDatabaseAsync();
        await using AsyncServiceScope scope = Factory.Services
            .CreateAsyncScope();
        CynaraIdentityDbContext identity = scope.ServiceProvider
            .GetRequiredService<CynaraIdentityDbContext>();
        IdentityUser<Guid> firstUser = await CreateUserAsync(
            identity,
            "actor-1@cynara.dev");
        IdentityUser<Guid> secondUser = await CreateUserAsync(
            identity,
            "actor-2@cynara.dev");
        var hospitalId = Guid.NewGuid();

        await InsertMembershipAsync(
            identity,
            firstUser.Id,
            hospitalId,
            "doctor-taken",
            MembershipStatus.Active);

        PostgresException? violation = await TryInsertMembershipAsync(
            identity,
            secondUser.Id,
            hospitalId,
            "doctor-taken",
            MembershipStatus.Active);

        Assert.NotNull(violation);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, violation.SqlState);
    }

    [Fact]
    public async Task RevokedActorId_IsAdmittedAgainByActiveMembership()
    {
        await Factory.ResetDatabaseAsync();
        await using AsyncServiceScope scope = Factory.Services
            .CreateAsyncScope();
        CynaraIdentityDbContext identity = scope.ServiceProvider
            .GetRequiredService<CynaraIdentityDbContext>();
        IdentityUser<Guid> firstUser = await CreateUserAsync(
            identity,
            "reuse-1@cynara.dev");
        IdentityUser<Guid> secondUser = await CreateUserAsync(
            identity,
            "reuse-2@cynara.dev");
        var hospitalId = Guid.NewGuid();

        await InsertMembershipAsync(
            identity,
            firstUser.Id,
            hospitalId,
            "doctor-reuse",
            MembershipStatus.Revoked);

        PostgresException? violation = await TryInsertMembershipAsync(
            identity,
            secondUser.Id,
            hospitalId,
            "doctor-reuse",
            MembershipStatus.Active);

        Assert.Null(violation);
        List<string> actors = await identity.Memberships
            .Where(item => item.HospitalId == hospitalId)
            .Select(item => item.ActorId)
            .ToListAsync();
        Assert.Equal(2, actors.Count);
        Assert.All(
            actors,
            actor => Assert.Equal("doctor-reuse", actor));
    }

    private static async Task<IdentityUser<Guid>> CreateUserAsync(
        CynaraIdentityDbContext identity,
        string email)
    {
        var user = new IdentityUser<Guid>
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
        };
        identity.Users.Add(user);
        await identity.SaveChangesAsync().ConfigureAwait(false);
        return user;
    }

    private static Task<int> InsertMembershipAsync(
        CynaraIdentityDbContext identity,
        Guid userId,
        Guid hospitalId,
        string actorId,
        MembershipStatus status)
    {
        identity.Memberships.Add(
            NewMembership(userId, hospitalId, actorId, status));
        return identity.SaveChangesAsync();
    }

    private static async Task<PostgresException?> TryInsertMembershipAsync(
        CynaraIdentityDbContext identity,
        Guid userId,
        Guid hospitalId,
        string actorId,
        MembershipStatus status)
    {
        try
        {
            await InsertMembershipAsync(
                    identity,
                    userId,
                    hospitalId,
                    actorId,
                    status)
                .ConfigureAwait(false);
            return null;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException violation
                && string.Equals(
                    violation.SqlState,
                    PostgresErrorCodes.UniqueViolation,
                    StringComparison.Ordinal))
        {
            return violation;
        }
    }

    private static Membership NewMembership(
        Guid userId,
        Guid hospitalId,
        string actorId,
        MembershipStatus status)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new Membership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            HospitalId = hospitalId,
            ActorId = actorId,
            CreatedAt = now,
            Status = status,
            ActivatedAt = now,
            UpdatedAt = now,
            RevokedAt = status == MembershipStatus.Revoked ? now : null,
        };
    }
}
