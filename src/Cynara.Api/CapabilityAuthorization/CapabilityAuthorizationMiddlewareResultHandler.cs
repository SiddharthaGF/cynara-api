using Cynara.Api.Common.ErrorHandling;
using Cynara.Application.Modules.Capabilities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Cynara.Api.CapabilityAuthorization;

/// <summary>
/// Minimal-API counterpart of <see cref="CapabilityAuthorizationFilter"/>.
/// The native <c>RequireAuthorization(policy)</c> path short-circuits in the
/// authorization middleware, so a denied request never reaches the exception
/// handler that MVC denials flow through. This handler closes that gap for
/// any policy carrying a <see cref="CapabilityRequirement"/>: it records the
/// <c>access.denied</c> audit event through <see cref="IDeniedAccessAuditor"/>
/// and emits the same 403 envelope as a raised
/// <see cref="CapabilityForbiddenException"/>, so Stage 3 workflow denials
/// are audited and shaped exactly like Stage 2 endpoint denials. Non-
/// capability policies fall through to the framework default handler.
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
