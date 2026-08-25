using Cynara.Application.Modules.Invitations;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cynara.Infrastructure.Modules.Identity;

/// <summary>
/// Development-only invitation notification sink that records expiry
/// notices and their recipient actors in the log without external
/// transport; dormant outside Development. Notices never carry token
/// material.
/// </summary>
public sealed partial class DevelopmentInvitationNotifier(
    ILogger<DevelopmentInvitationNotifier> logger,
    IHostEnvironment environment) : IInvitationNotifier
{
    public Task InvitationExpiredAsync(
        InvitationExpiryNotice notice,
        IReadOnlyList<string> recipientActorIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notice);
        _ = cancellationToken;
        if (environment.IsDevelopment() && logger.IsEnabled(LogLevel.Information))
        {
            LogInvitationExpired(
                notice.Email,
                notice.LinkVersion,
                recipientActorIds.Count);
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Expiry notice for {Email}: v{LinkVersion} to {RecipientCount} holders")]
    private partial void LogInvitationExpired(
        string email,
        int linkVersion,
        int recipientCount);
}
