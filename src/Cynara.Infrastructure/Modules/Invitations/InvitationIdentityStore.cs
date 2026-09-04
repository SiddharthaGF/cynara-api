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
    UserManager<IdentityUser<Guid>> users,
    CynaraIdentityDbContext identityDbContext) : IInvitationIdentityStore
{
    public async Task<Guid?> FindUserIdByEmailAsync(
        string email,
        CancellationToken cancellationToken)
    {
        IdentityUser<Guid>? user = await users.FindByEmailAsync(email)
            .ConfigureAwait(false);
        return user?.Id;
    }

    public async Task<IReadOnlyList<string>> ValidatePasswordAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = new IdentityUser<Guid>
        {
            UserName = email,
            Email = email,
        };
        List<string> errors = [];
        foreach (IPasswordValidator<IdentityUser<Guid>> validator
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
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = new IdentityUser<Guid>
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
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
}
