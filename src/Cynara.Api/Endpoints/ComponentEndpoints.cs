using Cynara.Application;
using Cynara.Application.Components;

namespace Cynara.Api.Endpoints;

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
        IComponentService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ComponentSummaryDto created = await service.CreateAsync(request, GetActorId(httpContext), cancellationToken);
        return Results.Created($"/api/components/{created.Code}", created);
    }

    private static async Task<IResult> ListComponentsAsync(
        IComponentService service,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ComponentSummaryDto> components = await service.ListAsync(cancellationToken);
        return Results.Ok(components);
    }

    private static async Task<IResult> GetComponentAsync(
        string code,
        IComponentService service,
        CancellationToken cancellationToken)
    {
        ComponentSummaryDto component = await service.GetSummaryAsync(code, cancellationToken);
        return Results.Ok(component);
    }

    private static async Task<IResult> GetDraftAsync(
        string code,
        IComponentService service,
        CancellationToken cancellationToken)
    {
        ComponentVersionDto draft = await service.GetDraftAsync(code, cancellationToken);
        return Results.Ok(draft);
    }

    private static async Task<IResult> UpdateDraftAsync(
        string code,
        UpdateComponentDraftRequest request,
        IComponentService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ComponentVersionDto draft = await service.UpdateDraftAsync(code, request, GetActorId(httpContext), cancellationToken);
        return Results.Ok(draft);
    }

    private static async Task<IResult> PublishDraftAsync(
        string code,
        PublishComponentDraftRequest request,
        IComponentService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ComponentVersionDto published = await service.PublishDraftAsync(code, request, GetActorId(httpContext), cancellationToken);
        return Results.Ok(published);
    }

    private static async Task<IResult> CreateDraftAsync(
        string code,
        IComponentService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ComponentVersionDto draft = await service.CreateDraftFromLatestAsync(code, GetActorId(httpContext), cancellationToken);
        return Results.Created($"/api/components/{code}/draft", draft);
    }

    private static async Task<IResult> SoftDeleteDraftAsync(
        string code,
        IComponentService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        await service.SoftDeleteDraftAsync(code, GetActorId(httpContext), cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetVersionAsync(
        string code,
        string version,
        IComponentService service,
        CancellationToken cancellationToken)
    {
        ComponentVersionDto componentVersion = await service.GetVersionAsync(code, version, cancellationToken);
        return Results.Ok(componentVersion);
    }

    private static async Task<IResult> RetireVersionAsync(
        string code,
        string version,
        IComponentService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ComponentVersionDto retired = await service.RetireVersionAsync(code, version, GetActorId(httpContext), cancellationToken);
        return Results.Ok(retired);
    }

    private static string? GetActorId(HttpContext httpContext)
    {
        return httpContext.Request.Headers.TryGetValue("X-Actor-Id", out Microsoft.Extensions.Primitives.StringValues value)
            ? value.ToString()
            : null;
    }
}

internal static class ProblemDetailsMapping
{
    public static IResult FromException(CynaraException exception)
    {
        return exception switch
        {
            NotFoundException => Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status404NotFound,
                title: "Not found"),
            ConflictException => Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict"),
            ValidationException => Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation failed"),
            ConcurrencyException => Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Concurrency conflict"),
            InvalidStateException => Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Invalid state"),
            FormResponseValidationException validationException => Results.Json(
                new
                {
                    title = "Validation failed",
                    status = StatusCodes.Status400BadRequest,
                    detail = validationException.Message,
                    errors = validationException.Errors.Select(error => new
                    {
                        error.Code,
                        error.Path,
                        error.Message,
                    }),
                },
                statusCode: StatusCodes.Status400BadRequest),
            _ => Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Unexpected error"),
        };
    }
}
