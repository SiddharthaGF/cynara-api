using Cynara.Application;
using Cynara.Application.Forms;

namespace Cynara.Api.Endpoints;

internal static class FormEndpoints
{
    public static IEndpointRouteBuilder MapFormEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/forms").WithTags("Forms");

        _ = group.MapPost("/", CreateFormAsync);
        _ = group.MapGet("/", ListFormsAsync);
        _ = group.MapGet("/{code}", GetFormAsync);
        _ = group.MapGet("/{code}/draft", GetEditableVersionAsync);
        _ = group.MapPut("/{code}/draft", UpdateDraftAsync);
        _ = group.MapPost("/{code}/draft/submit-review", SubmitForReviewAsync);
        _ = group.MapPost("/{code}/draft/withdraw-review", WithdrawFromReviewAsync);
        _ = group.MapPost("/{code}/draft/reject-review", RejectReviewAsync);
        _ = group.MapPost("/{code}/draft/publish", PublishDraftAsync);
        _ = group.MapPost("/{code}/draft", CreateDraftAsync);
        _ = group.MapDelete("/{code}/draft", SoftDeleteDraftAsync);
        _ = group.MapGet("/{code}/versions/{version}", GetVersionAsync);
        _ = group.MapPost("/{code}/versions/{version}/retire", RetireVersionAsync);

        return endpoints;
    }

    private static async Task<IResult> CreateFormAsync(
        CreateFormRequest request,
        IFormService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        FormSummaryDto created = await service.CreateAsync(request, GetActorId(httpContext), cancellationToken);
        return Results.Created($"/api/forms/{created.Code}", created);
    }

    private static async Task<IResult> ListFormsAsync(
        IFormService service,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FormSummaryDto> forms = await service.ListAsync(cancellationToken);
        return Results.Ok(forms);
    }

    private static async Task<IResult> GetFormAsync(
        string code,
        IFormService service,
        CancellationToken cancellationToken)
    {
        FormSummaryDto form = await service.GetSummaryAsync(code, cancellationToken);
        return Results.Ok(form);
    }

    private static async Task<IResult> GetEditableVersionAsync(
        string code,
        IFormService service,
        CancellationToken cancellationToken)
    {
        FormVersionDto version = await service.GetEditableVersionAsync(code, cancellationToken);
        return Results.Ok(version);
    }

    private static async Task<IResult> UpdateDraftAsync(
        string code,
        UpdateFormDraftRequest request,
        IFormService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        FormVersionDto draft = await service.UpdateDraftAsync(code, request, GetActorId(httpContext), cancellationToken);
        return Results.Ok(draft);
    }

    private static async Task<IResult> SubmitForReviewAsync(
        string code,
        SubmitFormDraftForReviewRequest request,
        IFormService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        FormVersionDto review = await service.SubmitForReviewAsync(code, request, GetActorId(httpContext), cancellationToken);
        return Results.Ok(review);
    }

    private static async Task<IResult> WithdrawFromReviewAsync(
        string code,
        WithdrawFormDraftFromReviewRequest request,
        IFormService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        FormVersionDto draft = await service.WithdrawFromReviewAsync(code, request, GetActorId(httpContext), cancellationToken);
        return Results.Ok(draft);
    }

    private static async Task<IResult> RejectReviewAsync(
        string code,
        RejectFormReviewRequest request,
        IFormService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        FormVersionDto rejected = await service.RejectReviewAsync(code, request, GetActorId(httpContext), cancellationToken);
        return Results.Ok(rejected);
    }

    private static async Task<IResult> PublishDraftAsync(
        string code,
        PublishFormDraftRequest request,
        IFormService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        FormVersionDto published = await service.PublishDraftAsync(code, request, GetActorId(httpContext), cancellationToken);
        return Results.Ok(published);
    }

    private static async Task<IResult> CreateDraftAsync(
        string code,
        IFormService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        FormVersionDto draft = await service.CreateDraftFromLatestAsync(code, GetActorId(httpContext), cancellationToken);
        return Results.Created($"/api/forms/{code}/draft", draft);
    }

    private static async Task<IResult> SoftDeleteDraftAsync(
        string code,
        string? reason,
        IFormService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        await service.SoftDeleteDraftAsync(code, reason, GetActorId(httpContext), cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetVersionAsync(
        string code,
        string version,
        IFormService service,
        CancellationToken cancellationToken)
    {
        FormVersionDto formVersion = await service.GetVersionAsync(code, version, cancellationToken);
        return Results.Ok(formVersion);
    }

    private static async Task<IResult> RetireVersionAsync(
        string code,
        string version,
        IFormService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        FormVersionDto retired = await service.RetireVersionAsync(code, version, GetActorId(httpContext), cancellationToken);
        return Results.Ok(retired);
    }

    private static string? GetActorId(HttpContext httpContext)
    {
        return httpContext.Request.Headers.TryGetValue("X-Actor-Id", out Microsoft.Extensions.Primitives.StringValues value)
            ? value.ToString()
            : null;
    }
}
