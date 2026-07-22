using Cynara.Application.Audit;

namespace Cynara.Api.Modules.Audit;

internal static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/audit").WithTags("Audit");

        _ = group.MapGet("/events", ListAuditEventsAsync);

        return endpoints;
    }

    private static async Task<IResult> ListAuditEventsAsync(
        IAuditService service,
        string? resourceType,
        Guid? resourceId,
        string? actorId,
        int? limit,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AuditEventDto> events = await service.ListAsync(
            new AuditQuery(resourceType, resourceId, actorId, limit ?? 50),
            cancellationToken).ConfigureAwait(false);
        return Results.Ok(events);
    }
}
