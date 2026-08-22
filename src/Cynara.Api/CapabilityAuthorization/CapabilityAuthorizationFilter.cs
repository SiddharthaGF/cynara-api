using System.Reflection;

using Cynara.Application.Modules.Capabilities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Cynara.Api.CapabilityAuthorization;

/// <summary>
/// Endpoint-boundary authorization filter. Runs before model binding and
/// action invocation and enforces the capability declared by
/// <see cref="RequireCapabilityAttribute"/> (method-level metadata wins over
/// the controller-level declaration). Evaluation flows through the native
/// <see cref="IAuthorizationService"/> pipeline — the policy is synthesized
/// on demand by <see cref="CapabilityPolicyProvider"/> and checked by
/// <see cref="CapabilityAuthorizationHandler"/>. Denials throw
/// <see cref="CapabilityForbiddenException"/>; the shared exception handler
/// turns that into a 403 and records the denied-access audit event. Requests
/// with no actor identity or no grant resolve to deny, and the filter never
/// reveals whether the protected resource exists.
/// </summary>
public sealed class CapabilityAuthorizationFilter(
    ICurrentActor currentActor,
    IAuthorizationService authorizationService) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.ActionDescriptor is not Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor descriptor)
        {
            return;
        }

        RequireCapabilityAttribute? requirement =
            descriptor.MethodInfo.GetCustomAttribute<RequireCapabilityAttribute>(inherit: true)
            ?? descriptor.ControllerTypeInfo.GetCustomAttribute<RequireCapabilityAttribute>(inherit: true);
        if (requirement is null)
        {
            return;
        }

        // Challenge anonymous requests with 401 before evaluating the
        // capability so absent authentication is reported as such. The test
        // seam authenticates every request, so the header-based actor suites
        // keep behaving as before.
        if (context.HttpContext.User.Identity?.IsAuthenticated is not true)
        {
            context.Result = new ChallengeResult();
            return;
        }

        AuthorizationResult result = await authorizationService
            .AuthorizeAsync(
                context.HttpContext.User,
                context.HttpContext,
                requirement.Capability)
            .ConfigureAwait(false);
        if (result.Succeeded)
        {
            return;
        }

        throw new CapabilityForbiddenException(
            requirement.Capability,
            currentActor.ActorId);
    }
}
