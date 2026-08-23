using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Cynara.Api.Modules.Identity;

/// <summary>
/// Anonymous account recovery without account enumeration: the request
/// endpoint answers uniformly and only emails a token for eligible accounts;
/// reset accepts only a valid single-use Identity token. Routes live under
/// <c>/connect</c> so they stay public and never demand a hospital context.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("connect/account")]
public sealed class AccountRecoveryController(
    UserManager<IdentityUser<Guid>> userManager,
    IEmailSender<IdentityUser<Guid>> emailSender) : ControllerBase
{
    /// <summary>
    /// Handles a recovery request. Always returns the uniform success body;
    /// a reset token is generated and sent only for an eligible existing
    /// account and never appears in the HTTP response.
    /// </summary>
    [HttpPost("~/connect/account/recovery")]
    [ProducesResponseType(typeof(AccountRecoveryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RecoveryAsync(
        [FromBody] AccountRecoveryRequest request)
    {
        IdentityUser<Guid>? user = await userManager
            .FindByNameAsync(request.Account ?? string.Empty)
            .ConfigureAwait(false);

        if (user is { EmailConfirmed: true })
        {
            string token = await userManager
                .GeneratePasswordResetTokenAsync(user)
                .ConfigureAwait(false);
            await emailSender
                .SendPasswordResetCodeAsync(
                    user,
                    user.Email ?? string.Empty,
                    token)
                .ConfigureAwait(false);
        }

        return Ok(new AccountRecoveryResponse(
            "If the account exists, a password reset message has been sent."));
    }

    /// <summary>
    /// Applies a password reset for a valid single-use token; all failure
    /// modes yield one bounded generic error. On success the security stamp
    /// rotates so every outstanding token dies with this reset.
    /// </summary>
    [HttpPost("reset")]
    [ProducesResponseType(typeof(AccountRecoveryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AccountRecoveryResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetAsync(
        [FromBody] AccountResetRequest request)
    {
        IdentityUser<Guid>? user = await userManager
            .FindByNameAsync(request.Account ?? string.Empty)
            .ConfigureAwait(false);

        if (user is not null)
        {
            IdentityResult result = await userManager
                .ResetPasswordAsync(
                    user,
                    request.Token ?? string.Empty,
                    request.NewPassword ?? string.Empty)
                .ConfigureAwait(false);
            if (result.Succeeded)
            {
                _ = await userManager
                    .UpdateSecurityStampAsync(user)
                    .ConfigureAwait(false);
                return Ok(new AccountRecoveryResponse("Password updated."));
            }
        }

        return BadRequest(new AccountRecoveryResponse(
            "The reset request could not be completed."));
    }
}

/// <summary>Body of a password-recovery request.</summary>
/// <remarks>
/// Mutable class (not a positional record): MVC model binding does not
/// support constructor metadata for <c>[FromBody]</c> parameters.
/// </remarks>
public sealed class AccountRecoveryRequest
{
    public string? Account { get; set; }
}

/// <summary>Body of a password-reset request.</summary>
/// <remarks>
/// Mutable class for the same model-binding reason as
/// <see cref="AccountRecoveryRequest"/>.
/// </remarks>
public sealed class AccountResetRequest
{
    public string? Account { get; set; }

    public string? Token { get; set; }

    public string? NewPassword { get; set; }
}

/// <summary>Uniform recovery/reset response payload.</summary>
public sealed record AccountRecoveryResponse(string Detail);
