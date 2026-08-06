using Cynara.Application.Modules.Capabilities;

using Microsoft.AspNetCore.Authorization;

namespace Cynara.Api.CapabilityAuthorization;

/// <summary>
/// Evaluates a <see cref="CapabilityRequirement"/> against the current actor's
/// effective capability set. Resolution is delegated to the scoped
/// <see cref="IEffectiveCapabilityResolver"/> so the endpoint-level filter,
/// the domain-level <see cref="CapabilityGuard"/>, and the
/// <c>GET /api/me/capabilities</c> endpoint keep sharing a single lookup per
/// request. The check never inspects <see cref="AuthorizationHandlerContext.User"/>
/// claims: actor and tenant come from the request context.
/// </summary>
public sealed class CapabilityAuthorizationHandler(
    IEffectiveCapabilityResolver resolver) : AuthorizationHandler<CapabilityRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CapabilityRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        CancellationToken cancellationToken =
            context.Resource is HttpContext { RequestAborted: var token }
                ? token
                : CancellationToken.None;

        bool granted = await resolver
            .HasCapabilityAsync(requirement.Capability, cancellationToken)
            .ConfigureAwait(false);
        if (granted)
        {
            context.Succeed(requirement);
            return;
        }

        context.Fail();
    }
}
