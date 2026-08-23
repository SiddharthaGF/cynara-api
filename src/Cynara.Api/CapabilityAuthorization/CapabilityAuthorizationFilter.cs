using System.Reflection;

using Cynara.Application.Modules.Capabilities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Cynara.Api.CapabilityAuthorization;

/// <summary>
/// Endpoint-boundary filter enforcing <see cref="RequireCapabilityAttribute"/>
/// (method-level metadata wins over the controller level) through the native
/// <see cref="IAuthorizationService"/> pipeline; denials produce a 403 with a
/// denied-access audit event, never revealing that the resource exists.
/// </summary>
public sealed class CapabilityAuthorizationFilter(
    ICurrentActor currentActor,
    IAuthorizationService authorizationService) : IAsyncAuthorizationFilter
{
    /// <summary>
    /// Challenges anonymous requests with 401 before evaluating the
    /// capability so absent authentication is reported as such; the test seam
    /// authenticates every request, so header-based actor suites behave as
    /// before.
    /// </summary>
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
