using Cynara.Api.Common.ActorContext;
using Cynara.Application.Modules.Invitations;
using Cynara.Domain.Capabilities;

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
}
