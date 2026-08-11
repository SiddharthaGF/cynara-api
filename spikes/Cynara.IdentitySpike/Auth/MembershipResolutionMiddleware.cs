using Cynara.Application.Modules.Hospitals;
using Cynara.Domain.Hospitals;
using Cynara.IdentitySpike.Data;
using Cynara.IdentitySpike.Domain;

using Microsoft.EntityFrameworkCore;

namespace Cynara.IdentitySpike.Auth;

/// <summary>
/// Resolves the authenticated principal into the Cynara request context.
/// Reads the Identity <c>sub</c> claim and the <c>X-Hospital-Code</c> header,
/// looks up the matching <see cref="Membership"/>, stamps the scoped
/// <see cref="HospitalContext"/> and <see cref="ResolvedActor"/>, and then
/// lets the unmodified <c>EffectiveCapabilityResolver</c> /
/// <c>CapabilityGuard</c> run against the standard (hospital, actor) pair.
/// Anonymous requests pass through untouched and stay deny-by-default.
/// </summary>
public sealed class MembershipResolutionMiddleware(RequestDelegate next)
{
    /// <summary>Header used to select the hospital workspace.</summary>
    public const string HospitalHeaderName = "X-Hospital-Code";

    /// <summary>
    /// Resolves the authenticated principal into the request context.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.User.Identity?.IsAuthenticated == true)
        {
            // OpenIddict does not apply the default inbound claim-type
            // mapping, so the subject claim is read by its literal name.
            string? subject = context.User.FindFirst("sub")?.Value;
            if (Guid.TryParse(subject, out Guid userId))
            {
                string? hospitalCode = context.Request.Headers[HospitalHeaderName]
                    .ToString();
                if (string.IsNullOrWhiteSpace(hospitalCode))
                {
                    await RejectAsync(
                        context,
                        StatusCodes.Status400BadRequest,
                        "Hospital context required.").ConfigureAwait(false);
                    return;
                }

                SpikeDbContext dbContext = context.RequestServices
                    .GetRequiredService<SpikeDbContext>();
                Membership? membership = await dbContext.Memberships
                    .AsNoTracking()
                    .Join(
                        dbContext.Hospitals.AsNoTracking(),
                        item => item.HospitalId,
                        hospital => hospital.Id,
                        (item, hospital) => new { Membership = item, Hospital = hospital })
                    .Where(row => row.Membership.UserId == userId
                        && row.Hospital.Code == hospitalCode)
                    .Select(row => row.Membership)
                    .FirstOrDefaultAsync(cancellationToken: context.RequestAborted)
                    .ConfigureAwait(false);

                if (membership is null)
                {
                    await RejectAsync(
                        context,
                        StatusCodes.Status400BadRequest,
                        "Unknown hospital workspace for this user.")
                        .ConfigureAwait(false);
                    return;
                }

                Hospital hospital = await dbContext.Hospitals
                    .AsNoTracking()
                    .FirstAsync(
                        item => item.Id == membership.HospitalId,
                        cancellationToken: context.RequestAborted)
                    .ConfigureAwait(false);

                HospitalContext hospitalContext = context.RequestServices
                    .GetRequiredService<HospitalContext>();
                hospitalContext.SetWorkspace(
                    hospital.Id,
                    hospital.Code,
                    hospital.Name);
                context.RequestServices.GetRequiredService<ResolvedActor>()
                    .ActorId = membership.ActorId;
            }
        }

        await next(context).ConfigureAwait(false);
    }

    private static async Task RejectAsync(
        HttpContext context,
        int statusCode,
        string detail)
    {
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new
        {
            title = "Hospital context required",
            status = statusCode,
            detail,
        }).ConfigureAwait(false);
    }
}
