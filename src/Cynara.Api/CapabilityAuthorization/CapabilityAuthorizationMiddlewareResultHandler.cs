using Cynara.Api.Common.ErrorHandling;
using Cynara.Application.Modules.Capabilities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Cynara.Api.CapabilityAuthorization;

/// <summary>
/// Minimal-API counterpart of <see cref="CapabilityAuthorizationFilter"/>:
/// native <c>RequireAuthorization</c> denials bypass the exception handler,
/// so this records the denied-access audit event and emits the same 403
/// envelope as <see cref="CapabilityForbiddenException"/> for capability
/// policies; other policies fall through to the framework default handler.
/// </summary>
public sealed class CapabilityAuthorizationMiddlewareResultHandler
    : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler fallback = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        if (!authorizeResult.Succeeded)
        {
            CapabilityRequirement? requirement = policy.Requirements
                .OfType<CapabilityRequirement>()
                .SingleOrDefault();
            if (requirement is not null)
            {
                IServiceProvider services = context.RequestServices;
                string? actorId = services
                    .GetRequiredService<ICurrentActor>()
                    .ActorId;

                IDeniedAccessAuditor auditor = services
                    .GetRequiredService<IDeniedAccessAuditor>();
                await auditor.RecordAsync(
                    requirement.Capability,
                    actorId,
                    context.Request.Path,
                    context.RequestAborted).ConfigureAwait(false);

                IResult result = ProblemDetailsMapping.FromException(
                    new CapabilityForbiddenException(
                        requirement.Capability,
                        actorId));
                await result.ExecuteAsync(context).ConfigureAwait(false);
                return;
            }
        }

        await fallback.HandleAsync(next, context, policy, authorizeResult)
            .ConfigureAwait(false);
    }
}
