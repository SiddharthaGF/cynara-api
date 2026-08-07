using Cynara.Api.Common.ActorContext;
using Cynara.Application.Modules.Workflows;
using Cynara.Domain.Capabilities;

namespace Cynara.Api.Modules.Pipelines;

/// <summary>
/// HTTP surface for the workflow pipeline runtime. Start pins the exact
/// published workflow version; advance evaluates decision conditions
/// server-side; complete/cancel/enter-in-error drive the explicit lifecycle.
/// All routes are hospital-scoped and capability-gated; the service layer
/// re-checks capabilities so denial never leaks resource existence.
/// </summary>
internal static class PipelinesEndpoints
{
    public static IEndpointRouteBuilder MapPipelinesEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder pipelines = endpoints
            .MapGroup("/api/pipelines")
            .WithTags("Pipelines");

        _ = pipelines.MapPost("/", StartPipelineAsync)
            .RequireAuthorization(CapabilityCodes.PipelinesWrite)
            .WithName("StartPipeline")
            .WithSummary("Start a workflow pipeline pinned to a published version")
            .Produces<PipelineDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        _ = pipelines.MapGet("/", ListPipelinesAsync)
            .RequireAuthorization(CapabilityCodes.PipelinesRead)
            .WithName("ListPipelines")
            .WithSummary("List workflow pipelines in the hospital workspace")
            .Produces<PipelineListResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden);

        _ = pipelines.MapGet("/journey", GetPipelineJourneyAsync)
            .RequireAuthorization(CapabilityCodes.PipelinesRead)
            .WithName("GetPipelineJourney")
            .WithSummary(
                "Get the patient or encounter pipeline journey rendered from "
                + "the exact published workflow version")
            .Produces<PatientJourneyResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        _ = pipelines.MapGet("/{id:guid}", GetPipelineAsync)
            .RequireAuthorization(CapabilityCodes.PipelinesRead)
            .WithName("GetPipeline")
            .WithSummary("Get one workflow pipeline")
            .Produces<PipelineDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        _ = pipelines.MapGet("/{id:guid}/history", GetPipelineHistoryAsync)
            .RequireAuthorization(CapabilityCodes.PipelinesRead)
            .WithName("GetPipelineHistory")
            .WithSummary("Get the append-only progression history of a pipeline")
            .Produces<PipelineHistoryResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        _ = pipelines.MapPost("/{id:guid}/advance", AdvancePipelineAsync)
            .RequireAuthorization(CapabilityCodes.PipelinesWrite)
            .WithName("AdvancePipeline")
            .WithSummary("Advance a running pipeline one step along the workflow graph")
            .Produces<PipelineDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        _ = pipelines.MapPost("/{id:guid}/complete", CompletePipelineAsync)
            .RequireAuthorization(CapabilityCodes.PipelinesWrite)
            .WithName("CompletePipeline")
            .WithSummary("Complete a running pipeline")
            .Produces<PipelineDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        _ = pipelines.MapPost("/{id:guid}/cancel", CancelPipelineAsync)
            .RequireAuthorization(CapabilityCodes.PipelinesWrite)
            .WithName("CancelPipeline")
            .WithSummary("Cancel a running pipeline")
            .Produces<PipelineDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        _ = pipelines.MapPost("/{id:guid}/enter-in-error", EnterInErrorPipelineAsync)
            .RequireAuthorization(CapabilityCodes.PipelinesWrite)
            .WithName("EnterInErrorPipeline")
            .WithSummary("Enter a running pipeline in error")
            .Produces<PipelineDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> StartPipelineAsync(
        StartPipelineRequest request,
        IPipelineService pipelines,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        PipelineDto dto = await pipelines.StartAsync(
            request,
            http.GetActorId(),
            cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/pipelines/{dto.Id}", dto);
    }

    private static async Task<IResult> ListPipelinesAsync(
        string? subjectType,
        Guid? subjectId,
        string? status,
        Guid? patientId,
        Guid? encounterId,
        IPipelineService pipelines,
        CancellationToken cancellationToken)
    {
        PipelineListResponse response = await pipelines.ListAsync(
            new PipelineListRequest(
                subjectType,
                subjectId,
                status,
                patientId,
                encounterId),
            cancellationToken).ConfigureAwait(false);
        return Results.Ok(response);
    }

    private static async Task<IResult> GetPipelineJourneyAsync(
        Guid? patientId,
        Guid? encounterId,
        IPipelineService pipelines,
        CancellationToken cancellationToken)
    {
        if (patientId is not null && encounterId is not null)
        {
            return Results.BadRequest(
                "Specify either patientId or encounterId, not both.");
        }

        if (patientId is Guid resolvedPatientId)
        {
            PatientJourneyResponse response = await pipelines
                .GetPatientJourneyAsync(
                    resolvedPatientId,
                    cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(response);
        }

        if (encounterId is Guid resolvedEncounterId)
        {
            EncounterJourneyResponse response = await pipelines
                .GetEncounterJourneyAsync(
                    resolvedEncounterId,
                    cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(response);
        }

        return Results.BadRequest(
            "Specify either patientId or encounterId.");
    }

    private static async Task<IResult> GetPipelineAsync(
        Guid id,
        IPipelineService pipelines,
        CancellationToken cancellationToken)
    {
        PipelineDto dto = await pipelines.GetAsync(id, cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(dto);
    }

    private static async Task<IResult> GetPipelineHistoryAsync(
        Guid id,
        IPipelineService pipelines,
        CancellationToken cancellationToken)
    {
        PipelineHistoryResponse response = await pipelines
            .ListHistoryAsync(id, cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(response);
    }

    private static async Task<IResult> AdvancePipelineAsync(
        Guid id,
        AdvancePipelineRequest request,
        IPipelineService pipelines,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        PipelineDto dto = await pipelines.AdvanceAsync(
            id,
            request,
            http.GetActorId(),
            cancellationToken).ConfigureAwait(false);
        return Results.Ok(dto);
    }

    private static async Task<IResult> CompletePipelineAsync(
        Guid id,
        TransitionPipelineRequest request,
        IPipelineService pipelines,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        PipelineDto dto = await pipelines.CompleteAsync(
            id,
            request,
            http.GetActorId(),
            cancellationToken).ConfigureAwait(false);
        return Results.Ok(dto);
    }

    private static async Task<IResult> CancelPipelineAsync(
        Guid id,
        TransitionPipelineRequest request,
        IPipelineService pipelines,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        PipelineDto dto = await pipelines.CancelAsync(
            id,
            request,
            http.GetActorId(),
            cancellationToken).ConfigureAwait(false);
        return Results.Ok(dto);
    }

    private static async Task<IResult> EnterInErrorPipelineAsync(
        Guid id,
        TransitionPipelineRequest request,
        IPipelineService pipelines,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        PipelineDto dto = await pipelines.EnterInErrorAsync(
            id,
            request,
            http.GetActorId(),
            cancellationToken).ConfigureAwait(false);
        return Results.Ok(dto);
    }
}
