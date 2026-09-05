using Cynara.Application.Modules.Invitations;
using Cynara.Application.Modules.Invitations.Persistence;
using Cynara.Domain.Memberships;
using Cynara.Infrastructure.Modules.Identity;

using Microsoft.AspNetCore.Identity;

namespace Cynara.Infrastructure.Modules.Invitations;

/// <summary>
/// EF/Identity-backed implementation of the acceptance identity store.
/// UserManager's internal saves ride the shared acceptance transaction
/// because the identity context is attached to it; the workflow calls
/// <see cref="IInvitationIdentityStore.SaveChangesAsync"/> once to flush
/// staged memberships inside the same commit.
/// </summary>
public sealed class InvitationIdentityStore(
    UserManager<CynaraUser> users,
    CynaraIdentityDbContext identityDbContext) : IInvitationIdentityStore
{
    public async Task<Guid?> FindUserIdByEmailAsync(
        string email,
        CancellationToken cancellationToken)
    {
        CynaraUser? user = await users.FindByEmailAsync(email)
            .ConfigureAwait(false);
        return user?.Id;
    }

    public async Task<IReadOnlyList<string>> ValidatePasswordAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = new CynaraUser
        {
            UserName = email,
            Email = email,
        };
        List<string> errors = [];
        foreach (IPasswordValidator<CynaraUser> validator
            in users.PasswordValidators)
        {
            IdentityResult result = await validator
                .ValidateAsync(users, user, password).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                errors.AddRange(result.Errors.Select(
                    static error => error.Description));
            }
        }

        return errors;
    }

    public async Task<CreateUserResult> CreateUserAsync(
        string email,
        string password,
        string? givenName,
        string? familyName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = new CynaraUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            GivenName = NormalizeName(givenName),
            FamilyName = NormalizeName(familyName),
        };
        IdentityResult result = await users.CreateAsync(user, password)
            .ConfigureAwait(false);
        if (result.Succeeded)
        {
            return new CreateUserResult(user.Id, [], Duplicate: false);
        }

        if (result.Errors.Any(static error =>
            error.Code is nameof(IdentityErrorDescriber.DuplicateUserName)
            or nameof(IdentityErrorDescriber.DuplicateEmail)))
        {
            return new CreateUserResult(UserId: null, [], Duplicate: true);
        }

        return new CreateUserResult(
            UserId: null,
            [.. result.Errors.Select(static error => error.Description)],
            Duplicate: false);
    }

    public async Task FillMissingNamesAsync(
        Guid userId,
        string? givenName,
        string? familyName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? given = NormalizeName(givenName);
        string? family = NormalizeName(familyName);
        if (given is null && family is null)
        {
            return;
        }

        CynaraUser? user = await users.FindByIdAsync(userId.ToString())
            .ConfigureAwait(false);
        if (user is null)
        {
            return;
        }

        bool changed = false;
        if (given is not null && string.IsNullOrWhiteSpace(user.GivenName))
        {
            user.GivenName = given;
            changed = true;
        }

        if (family is not null && string.IsNullOrWhiteSpace(user.FamilyName))
        {
            user.FamilyName = family;
            changed = true;
        }

        if (changed)
        {
            _ = await users.UpdateAsync(user).ConfigureAwait(false);
        }
    }

    public Task AddMembershipAsync(
        Guid userId,
        Guid hospitalId,
        string actorId,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        _ = identityDbContext.Memberships.Add(new Membership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            HospitalId = hospitalId,
            ActorId = actorId,
            CreatedAt = createdAt,
            Status = MembershipStatus.Active,
            ActivatedAt = createdAt,
            UpdatedAt = now,
        });
        return Task.CompletedTask;
    }

    public Task<bool> IsActorIdTakenAsync(
        Guid hospitalId,
        string actorId,
        CancellationToken cancellationToken)
    {
        return identityDbContext.Memberships
            .AsNoTracking()
            .AnyAsync(
                item => item.HospitalId == hospitalId
                    && item.ActorId == actorId
                    && item.Status == MembershipStatus.Active,
                cancellationToken);
    }

    public Task<bool> HasMembershipAsync(
        Guid userId,
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        return identityDbContext.Memberships
            .AsNoTracking()
            .AnyAsync(
                item => item.UserId == userId
                    && item.HospitalId == hospitalId
                    && item.Status == MembershipStatus.Active,
                cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return identityDbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Trims informational names; blank collapses to null so columns stay
    /// null instead of storing whitespace.
    /// </summary>
    private static string? NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
