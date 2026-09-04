using Cynara.Api.Common.ActorContext;
using Cynara.Api.Common.ErrorHandling;
using Cynara.Application;
using Cynara.Application.Modules.Invitations;
using Cynara.Domain.Capabilities;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Cynara.Api.Modules.Invitations;

/// <summary>
/// HTTP surface of the administrative invitation lifecycle. Routes carry
/// the capability-code authorization policy so denials short-circuit into
/// the shared 403 envelope before any data is touched, and the workflow
/// re-checks tenant context and capability so denial never leaks resource
/// existence. Link tokens appear only in create/resend responses — never
/// in listings, logs, or audit metadata.
/// </summary>
internal static class InvitationsEndpoints
{
    public static IEndpointRouteBuilder MapInvitationsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder invitations = endpoints
            .MapGroup("/api/user-invitations")
            .WithTags("User Invitations");

        _ = invitations.MapGet("/", ListAsync)
            .RequireAuthorization(CapabilityCodes.UserInvitationsRead)
            .WithName("ListInvitations")
            .WithSummary("List the workspace's invitations newest-first")
            .Produces<IReadOnlyList<InvitationView>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden);

        _ = invitations.MapPost("/", CreateAsync)
            .RequireAuthorization(CapabilityCodes.UserInvitationsWrite)
            .WithName("CreateInvitation")
            .WithSummary("Issue a pending invitation with a 72-hour link")
            .Produces<CreateInvitationResult>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden);

        _ = invitations.MapPost("/{id:guid}/cancel", CancelAsync)
            .RequireAuthorization(CapabilityCodes.UserInvitationsWrite)
            .WithName("CancelInvitation")
            .WithSummary("Cancel a pending or expired invitation")
            .Produces<InvitationView>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        _ = invitations.MapPost("/{id:guid}/resend", ResendAsync)
            .RequireAuthorization(CapabilityCodes.UserInvitationsWrite)
            .WithName("ResendInvitation")
            .WithSummary("Supersede the current link and restart validity")
            .Produces<CreateInvitationResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        _ = invitations.MapPost("/{token}/accept", AcceptAsync)
            .AllowAnonymous()
            .WithName("AcceptInvitation")
            .WithSummary("Accept an invitation and join the hospital")
            .Produces<AcceptInvitationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status429TooManyRequests);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        InvitationAdminWorkflow workflow,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<InvitationView> items = await workflow
            .ListAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(items);
    }

    private static async Task<IResult> CreateAsync(
        CreateInvitationRequest request,
        InvitationAdminWorkflow workflow,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        CreateInvitationResult created = await workflow.CreateAsync(
            request,
            http.GetActorId(),
            cancellationToken).ConfigureAwait(false);
        return Results.Created(
            $"/api/user-invitations/{created.Invitation.Id}",
            created);
    }

    private static async Task<IResult> CancelAsync(
        Guid id,
        InvitationAdminWorkflow workflow,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        InvitationView cancelled = await workflow.CancelAsync(
            id,
            http.GetActorId(),
            cancellationToken).ConfigureAwait(false);
        return Results.Ok(cancelled);
    }

    private static async Task<IResult> ResendAsync(
        Guid id,
        InvitationAdminWorkflow workflow,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        CreateInvitationResult resent = await workflow.ResendAsync(
            id,
            http.GetActorId(),
            cancellationToken).ConfigureAwait(false);
        return Results.Ok(resent);
    }

    /// <summary>
    /// Anonymous acceptance: turns the one-time link into credentials and a
    /// hospital membership. Every token-state outcome returns the same
    /// uniform envelope so the token space stays unenumerable; concurrency
    /// losers and unique-violation races are folded into the envelope or a
    /// 400 here, never a 5xx.
    /// </summary>
    private static async Task<IResult> AcceptAsync(
        string token,
        AcceptInvitationRequest request,
        InvitationAcceptanceWorkflow workflow,
        CancellationToken cancellationToken)
    {
        try
        {
            AcceptInvitationResponse response = await workflow.AcceptAsync(
                token, request, cancellationToken).ConfigureAwait(false);
            return Results.Ok(response);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Ok(AcceptInvitationResponse.Failure);
        }
        catch (ConcurrencyException)
        {
            return Results.Ok(AcceptInvitationResponse.Failure);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException postgres
                && string.Equals(postgres.SqlState, PostgresErrorCodes.UniqueViolation, StringComparison.Ordinal))
        {
            return ProblemDetailsMapping.FromException(
                new ValidationException(
                    "The invited user or actor already exists in this "
                    + "hospital."));
        }
    }
}
