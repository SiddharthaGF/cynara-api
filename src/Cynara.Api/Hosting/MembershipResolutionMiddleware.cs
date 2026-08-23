using Cynara.Api.Common.ActorContext;
using Cynara.Application.Modules.Hospitals;

namespace Cynara.Api.Hosting;

/// <summary>
/// Resolves the authenticated principal into the hospital-scoped request
/// actor via <see cref="IHospitalMembershipReader"/>, stamping the scoped
/// <see cref="ResolvedActor"/> so capability resolution and audit attribution
/// key on it; users without membership are denied with 403.
/// </summary>
/// <remarks>
/// OpenIddict does not apply the default inbound claim-type mapping, so the
/// subject claim is read by its literal name; client-credentials subjects are
/// not GUIDs, keep an empty actor, and cannot do capability work downstream.
/// </remarks>
internal sealed class MembershipResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (AuthPathPolicy.IsPublicPath(context.Request.Path)
            || AuthPathPolicy.IsTenantExemptPath(context.Request.Path))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (context.User.Identity?.IsAuthenticated is not true)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (!PrincipalSubject.TryGetUserId(context.User, out Guid userId))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        IHospitalContext hospitalContext = context.RequestServices
            .GetRequiredService<IHospitalContext>();
        if (!hospitalContext.IsResolved)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        string? actorId = await context.RequestServices
            .GetRequiredService<IHospitalMembershipReader>()
            .FindActorIdAsync(
                userId,
                hospitalContext.HospitalId,
                context.RequestAborted)
            .ConfigureAwait(false);

        if (actorId is null)
        {
            await RejectNoMembershipAsync(context).ConfigureAwait(false);
            return;
        }

        context.RequestServices.GetRequiredService<ResolvedActor>().ActorId = actorId;

        await next(context).ConfigureAwait(false);
    }

    private static async Task RejectNoMembershipAsync(HttpContext context)
    {
        const string detail =
            "The authenticated user has no membership in the "
            + "resolved hospital workspace.";

        await JsonApiErrorResponse.WriteAsync(
            context,
            StatusCodes.Status403Forbidden,
            "No hospital membership",
            detail)
            .ConfigureAwait(false);
    }
}
