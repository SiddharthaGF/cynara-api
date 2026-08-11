using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.Hospitals;
using Cynara.Domain.Capabilities;

namespace Cynara.IdentitySpike.Endpoints;

/// <summary>
/// Capability-protected demo endpoints. Each route runs the unmodified
/// <see cref="CapabilityGuard"/> against the principal-resolved actor and
/// hospital context; a denial surfaces as a 403 problem-details envelope.
/// </summary>
public static class ProtectedEndpoints
{
    /// <summary>Maps the capability-protected demo endpoints.</summary>
    public static IEndpointRouteBuilder MapProtectedEndpoints(
        this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        _ = app.MapGet("/api/patients", async (
            ICapabilityGuard guard,
            ICurrentActor currentActor,
            IHospitalContext hospitalContext,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await guard.RequireAsync(
                    CapabilityCodes.PatientsRead,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (CapabilityForbiddenException exception)
            {
                return ToForbidden(exception);
            }

            return Results.Ok(new
            {
                message = "patients.read granted",
                actorId = currentActor.ActorId,
                hospitalCode = hospitalContext.Code,
            });
        }).RequireAuthorization();

        _ = app.MapGet("/api/encounters", async (
            ICapabilityGuard guard,
            ICurrentActor currentActor,
            IHospitalContext hospitalContext,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await guard.RequireAsync(
                    CapabilityCodes.EncountersWrite,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (CapabilityForbiddenException exception)
            {
                return ToForbidden(exception);
            }

            return Results.Ok(new
            {
                message = "encounters.write granted",
                actorId = currentActor.ActorId,
                hospitalCode = hospitalContext.Code,
            });
        }).RequireAuthorization();

        return app;
    }

    private static IResult ToForbidden(
        CapabilityForbiddenException exception)
    {
        return Results.Problem(
        statusCode: StatusCodes.Status403Forbidden,
        title: exception.Title,
        detail: $"Capability '{exception.Capability}' is required.",
        extensions: new Dictionary<string, object?>(
StringComparer.Ordinal)
        {
            ["actorId"] = exception.ActorId,
        });
    }
}
