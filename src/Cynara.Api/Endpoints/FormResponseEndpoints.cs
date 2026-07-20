using Cynara.Application.Forms;

namespace Cynara.Api.Endpoints;

internal static class FormResponseEndpoints
{
    public static IEndpointRouteBuilder MapFormResponseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder formsGroup = endpoints.MapGroup("/api/forms").WithTags("Form Responses");

        _ = formsGroup.MapPost("/{code}/versions/{version}/responses", CreateResponseAsync);

        RouteGroupBuilder responsesGroup = endpoints.MapGroup("/api/responses").WithTags("Form Responses");

        _ = responsesGroup.MapGet("/{id:guid}", GetResponseAsync);
        _ = responsesGroup.MapPut("/{id:guid}", UpdateResponseAsync);
        _ = responsesGroup.MapPost("/{id:guid}/complete", CompleteResponseAsync);
        _ = responsesGroup.MapDelete("/{id:guid}", SoftDeleteResponseAsync);
        _ = responsesGroup.MapGet("/{id:guid}/revisions", ListRevisionsAsync);
        _ = responsesGroup.MapGet("/{id:guid}/revisions/{revisionNumber:int}", GetRevisionAsync);

        return endpoints;
    }

    private static async Task<IResult> CreateResponseAsync(
        string code,
        string version,
        CreateFormResponseRequest request,
        IFormResponseService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        FormResponseDto created = await service.CreateAsync(
            code,
            version,
            request,
            GetActorId(httpContext),
            cancellationToken);
        return Results.Created($"/api/responses/{created.Id}", created);
    }

    private static async Task<IResult> GetResponseAsync(
        Guid id,
        IFormResponseService service,
        CancellationToken cancellationToken,
        bool includeDeleted = false)
    {
        FormResponseDto response = await service.GetAsync(id, includeDeleted, cancellationToken);
        return Results.Ok(response);
    }

    private static async Task<IResult> UpdateResponseAsync(
        Guid id,
        UpdateFormResponseRequest request,
        IFormResponseService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        FormResponseDto updated = await service.UpdateAsync(id, request, GetActorId(httpContext), cancellationToken);
        return Results.Ok(updated);
    }

    private static async Task<IResult> CompleteResponseAsync(
        Guid id,
        CompleteFormResponseRequest request,
        IFormResponseService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        FormResponseDto completed = await service.CompleteAsync(id, request, GetActorId(httpContext), cancellationToken);
        return Results.Ok(completed);
    }

    private static async Task<IResult> SoftDeleteResponseAsync(
        Guid id,
        string? reason,
        IFormResponseService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        await service.SoftDeleteDraftAsync(id, reason, GetActorId(httpContext), cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ListRevisionsAsync(
        Guid id,
        IFormResponseService service,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FormResponseRevisionDto> revisions = await service.ListRevisionsAsync(id, cancellationToken);
        return Results.Ok(revisions);
    }

    private static async Task<IResult> GetRevisionAsync(
        Guid id,
        int revisionNumber,
        IFormResponseService service,
        CancellationToken cancellationToken)
    {
        if (revisionNumber < 0)
        {
            return Results.Problem(
                detail: "Revision number must be non-negative.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation failed");
        }

        FormResponseRevisionDto revision = await service.GetRevisionAsync(
            id,
            (uint)revisionNumber,
            cancellationToken);
        return Results.Ok(revision);
    }

    private static string? GetActorId(HttpContext httpContext)
    {
        return httpContext.Request.Headers.TryGetValue("X-Actor-Id", out Microsoft.Extensions.Primitives.StringValues value)
            ? value.ToString()
            : null;
    }
}
