using Cynara.Api.Common.ActorContext;
using Cynara.Application.Modules.Hospitals;
using Cynara.Infrastructure.Modules.Identity;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Api.Hosting;

/// <summary>
/// Resolves the authenticated principal into the request actor context.
/// Reads the Identity <c>sub</c> claim and combines it with the hospital
/// already resolved by <see cref="HospitalContextMiddleware"/>, looks up the
/// matching <see cref="Membership"/>, and stamps the scoped
/// <see cref="ResolvedActor"/> so capability resolution and audit attribution
/// key on the hospital-scoped actor. Authenticated users without a matching
/// membership are denied with 403. Public authentication/schema paths and
/// anonymous requests pass through untouched (deny-by-default downstream).
/// </summary>
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

        // OpenIddict does not apply the default inbound claim-type mapping, so
        // the subject claim is read by its literal name. Client-credentials
        // subjects are client identifiers (not user ids), so they are not
        // GUIDs and are ignored here — they retain an empty actor and are
        // denied capability work downstream.
        string? subject = context.User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(subject, out Guid userId))
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

        CynaraIdentityDbContext identity = context.RequestServices
            .GetRequiredService<CynaraIdentityDbContext>();
        Membership? membership = await identity.Memberships
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.UserId == userId
                    && item.HospitalId == hospitalContext.HospitalId,
                context.RequestAborted)
            .ConfigureAwait(false);

        if (membership is null)
        {
            await RejectNoMembershipAsync(context).ConfigureAwait(false);
            return;
        }

        context.RequestServices.GetRequiredService<ResolvedActor>().ActorId =
            membership.ActorId;

        await next(context).ConfigureAwait(false);
    }

    private static async Task RejectNoMembershipAsync(HttpContext context)
    {
        var document = new
        {
            errors = new[]
            {
                new
                {
                    status = StatusCodes.Status403Forbidden
                        .ToString(System.Globalization.CultureInfo.InvariantCulture),
                    title = "No hospital membership",
                    detail =
                        "The authenticated user has no membership in the "
                        + "resolved hospital workspace.",
                },
            },
        };

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/vnd.api+json";
        await context.Response.WriteAsJsonAsync(
            document,
            options: null,
            contentType: "application/vnd.api+json",
            cancellationToken: context.RequestAborted).ConfigureAwait(false);
    }
}
