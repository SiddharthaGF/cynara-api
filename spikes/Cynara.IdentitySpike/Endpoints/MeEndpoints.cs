using System.Security.Claims;

using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.Hospitals;

using Microsoft.AspNetCore.Mvc;

namespace Cynara.IdentitySpike.Endpoints;

/// <summary>
/// Current-user endpoint. Proves the full principal -> membership -> hospital
/// context -> actor -> capability chain against the unmodified Cynara
/// Application services.
/// </summary>
public static class MeEndpoints
{
    /// <summary>Maps the <c>GET /api/me</c> current-user endpoint.</summary>
    public static IEndpointRouteBuilder MapMeEndpoints(
        this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        _ = app.MapGet("/api/me", async (
            ClaimsPrincipal user,
            [FromServices] ICurrentActor currentActor,
            [FromServices] IHospitalContext hospitalContext,
            [FromServices] EffectiveCapabilityResolver resolver,
            CancellationToken cancellationToken) =>
        {
            IReadOnlySet<string> capabilities = await resolver
                .ResolveAsync(cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(new
            {
                userId = user.FindFirst("sub")?.Value,
                email = user.FindFirst("email")?.Value,
                hospital = hospitalContext.IsResolved
                    ? new
                    {
                        id = hospitalContext.HospitalId,
                        code = hospitalContext.Code,
                        name = hospitalContext.Name,
                    }
                    : null,
                actorId = currentActor.ActorId,
                capabilities = capabilities.Order(StringComparer.Ordinal),
            });
        }).RequireAuthorization();

        return app;
    }
}
