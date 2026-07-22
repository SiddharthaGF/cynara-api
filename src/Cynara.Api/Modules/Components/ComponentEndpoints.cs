using Cynara.Api.Common.ActorContext;
using Cynara.Application.Components;
using Cynara.Application.Modules.Components;

namespace Cynara.Api.Modules.Components;

internal static class ComponentEndpoints
{
    public static IEndpointRouteBuilder MapComponentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/components").WithTags("Components");

        _ = group.MapPost("/", CreateComponentAsync);
        _ = group.MapGet("/", ListComponentsAsync);
        _ = group.MapGet("/{code}", GetComponentAsync);
        _ = group.MapGet("/{code}/draft", GetDraftAsync);
        _ = group.MapPut("/{code}/draft", UpdateDraftAsync);
        _ = group.MapPost("/{code}/draft/publish", PublishDraftAsync);
        _ = group.MapPost("/{code}/draft", CreateDraftAsync);
        _ = group.MapDelete("/{code}/draft", SoftDeleteDraftAsync);
        _ = group.MapGet("/{code}/versions/{version}", GetVersionAsync);
        _ = group.MapPost("/{code}/versions/{version}/retire", RetireVersionAsync);

        return endpoints;
    }

    private static async Task<IResult> CreateComponentAsync(
        CreateComponentRequest request,
        IComponentLifecycleService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ComponentSummaryDto created = await service.CreateAsync(request, httpContext.GetActorId(), cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/components/{created.Code}", created);
    }

    private static async Task<IResult> ListComponentsAsync(
        IComponentQueryService service,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ComponentSummaryDto> components = await service.ListAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(components);
    }

    private static async Task<IResult> GetComponentAsync(
        string code,
        IComponentQueryService service,
        CancellationToken cancellationToken)
    {
        ComponentSummaryDto component = await service.GetSummaryAsync(code, cancellationToken).ConfigureAwait(false);
        return Results.Ok(component);
    }

    private static async Task<IResult> GetDraftAsync(
        string code,
        IComponentQueryService service,
        CancellationToken cancellationToken)
    {
        ComponentVersionDto draft = await service.GetDraftAsync(code, cancellationToken).ConfigureAwait(false);
        return Results.Ok(draft);
    }

    private static async Task<IResult> UpdateDraftAsync(
        string code,
        UpdateComponentDraftRequest request,
        IComponentLifecycleService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ComponentVersionDto draft = await service.UpdateDraftAsync(code, request, httpContext.GetActorId(), cancellationToken).ConfigureAwait(false);
        return Results.Ok(draft);
    }

    private static async Task<IResult> PublishDraftAsync(
        string code,
        PublishComponentDraftRequest request,
        IComponentLifecycleService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ComponentVersionDto published = await service.PublishDraftAsync(code, request, httpContext.GetActorId(), cancellationToken).ConfigureAwait(false);
        return Results.Ok(published);
    }

    private static async Task<IResult> CreateDraftAsync(
        string code,
        IComponentLifecycleService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ComponentVersionDto draft = await service.CreateDraftFromLatestAsync(code, httpContext.GetActorId(), cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/components/{code}/draft", draft);
    }

    private static async Task<IResult> SoftDeleteDraftAsync(
        string code,
        IComponentLifecycleService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        await service.SoftDeleteDraftAsync(code, httpContext.GetActorId(), cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> GetVersionAsync(
        string code,
        string version,
        IComponentQueryService service,
        CancellationToken cancellationToken)
    {
        ComponentVersionDto componentVersion = await service.GetVersionAsync(code, version, cancellationToken).ConfigureAwait(false);
        return Results.Ok(componentVersion);
    }

    private static async Task<IResult> RetireVersionAsync(
        string code,
        string version,
        IComponentLifecycleService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ComponentVersionDto retired = await service.RetireVersionAsync(code, version, httpContext.GetActorId(), cancellationToken).ConfigureAwait(false);
        return Results.Ok(retired);
    }
}
