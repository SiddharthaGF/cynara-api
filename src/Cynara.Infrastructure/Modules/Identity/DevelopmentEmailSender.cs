using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cynara.Infrastructure.Modules.Identity;

/// <summary>
/// Development-only <see cref="IEmailSender{TUser}"/> sink. Records the
/// would-be recovery message to the logger and performs no external
/// transport. Production email-provider configuration is future scope; this
/// sender never invents one and is dormant outside Development.
/// </summary>
public sealed class DevelopmentEmailSender(
    ILogger<DevelopmentEmailSender> logger,
    IHostEnvironment environment) : IEmailSender<IdentityUser<Guid>>
{
    /// <summary>Logs the confirmation link in Development.</summary>
    public Task SendConfirmationLinkAsync(
        IdentityUser<Guid> user,
        string email,
        string confirmationLink)
    {
        return SendAsync(user, email, "confirmation link", confirmationLink);
    }

    /// <summary>Logs the password-reset link in Development.</summary>
    public Task SendPasswordResetLinkAsync(
        IdentityUser<Guid> user,
        string email,
        string resetLink)
    {
        return SendAsync(user, email, "password reset link", resetLink);
    }

    /// <summary>Logs the password-reset code in Development.</summary>
    public Task SendPasswordResetCodeAsync(
        IdentityUser<Guid> user,
        string email,
        string resetCode)
    {
        return SendAsync(user, email, "password reset code", resetCode);
    }

    private Task SendAsync(
        IdentityUser<Guid> user,
        string email,
        string kind,
        string payload)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (environment.IsDevelopment() && logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Dev email sink for {Email}: {Kind} = {Payload}",
                email,
                kind,
                payload);
        }

        return Task.CompletedTask;
    }
}
