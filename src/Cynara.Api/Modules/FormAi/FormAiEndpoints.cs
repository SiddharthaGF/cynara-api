using Cynara.Application.Modules.FormAi;

namespace Cynara.Api.Modules.FormAi;

internal static class FormAiEndpoints
{
    public static IEndpointRouteBuilder MapFormAiEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder aiGroup = endpoints.MapGroup("/api/ai").WithTags("Form AI");
        _ = aiGroup.MapGet("/status", GetStatusAsync);
        _ = aiGroup.MapGet("/settings", GetSettingsAsync);
        _ = aiGroup.MapPut("/settings", UpdateSettingsAsync);

        RouteGroupBuilder formsGroup = endpoints.MapGroup("/api/forms").WithTags("Form AI");
        _ = formsGroup.MapPost("/{code}/draft/ai-chat", ChatAsync);
        _ = formsGroup.MapPost("/{code}/draft/ai-chat/stream", ChatStreamAsync)
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream");
        return endpoints;
    }

    private static async Task<IResult> GetStatusAsync(
        IFormAiService service,
        CancellationToken cancellationToken)
    {
        return Results.Ok(await service.GetStatusAsync(cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> GetSettingsAsync(
        IFormAiService service,
        CancellationToken cancellationToken)
    {
        return Results.Ok(await service.GetSettingsAsync(cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> UpdateSettingsAsync(
        FormAiSettingsUpdateRequest request,
        IFormAiService service,
        CancellationToken cancellationToken)
    {
        return Results.Ok(await service.UpdateSettingsAsync(request, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> ChatAsync(
        string code,
        FormAiChatRequest request,
        IFormAiService service,
        CancellationToken cancellationToken)
    {
        return Results.Ok(await service.ChatAsync(code, request, cancellationToken).ConfigureAwait(false));
    }

    private static async Task ChatStreamAsync(
        string code,
        FormAiChatRequest request,
        IFormAiService service,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        await service.ChatStreamAsync(code, request, response.Body, cancellationToken).ConfigureAwait(false);
    }
}
