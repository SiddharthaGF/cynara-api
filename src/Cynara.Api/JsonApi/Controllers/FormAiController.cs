using System.Text.Json;

using Cynara.Application.Modules.FormAi;
using Cynara.Application.Modules.Hospitals;
using Cynara.Domain.Forms;
using Cynara.Infrastructure.Persistence;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cynara.Api.JsonApi.Controllers;

/// <summary>
/// Intentional non-JSON:API Form AI endpoints.
/// Settings CRUD lives on JSON:API <c>/api/aiProviderSettings</c>.
/// Status is a lightweight plain-JSON readiness probe for the chat UI.
/// Chat returns a plain JSON turn; chat/stream uses SSE
/// (<c>text/event-stream</c>) and cannot be a JSON:API resource document.
/// Bodies are read with System.Text.Json to avoid JsonApiDotNetCore
/// ModelMetadataKind.Constructor failures on positional records.
/// </summary>
[ApiController]
[Route("api/ai")]
[Tags("Form AI")]
public sealed class FormAiController(
    IFormAiService formAiService,
    IHospitalContext hospitalContext,
    CynaraDbContext dbContext) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Returns whether AI is configured and which model/base URL are active
    /// without exposing the raw API key.
    /// </summary>
    [HttpGet("status", Name = "getFormAiStatus")]
    [EndpointDescription(
        "Non-resource readiness probe (application/json). Reports Form AI "
        + "configuration status for the active provider settings and "
        + "environment fallbacks. Never returns the raw API key. "
        + "Admin settings use JSON:API GET/PATCH /api/aiProviderSettings/{id}.")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(FormAiStatusResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<FormAiStatusResponse>> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        return Ok(await formAiService.GetStatusAsync(cancellationToken)
            .ConfigureAwait(false));
    }

    /// <summary>
    /// Runs a non-streaming AI authoring turn against a form's editable draft.
    /// </summary>
    [HttpPost("forms/{formDefinitionId:guid}/chat", Name = "postFormAiChat")]
    [EndpointDescription(
        "Non-resource RPC (application/json). Invokes Form AI chat against "
        + "the editable draft of the given form definition. Returns proposed "
        + "clinical/UI/rules schema updates. Not modeled as JSON:API because "
        + "the payload is a command/result turn, not a persisted resource.")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(FormAiChatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FormAiChatResponse>> ChatAsync(
        Guid formDefinitionId,
        CancellationToken cancellationToken)
    {
        FormAiChatRequest request = await ReadBodyAsync<FormAiChatRequest>(
            cancellationToken).ConfigureAwait(false);
        string code = await RequireFormCodeAsync(formDefinitionId, cancellationToken)
            .ConfigureAwait(false);
        return Ok(await formAiService.ChatAsync(code, request, cancellationToken)
            .ConfigureAwait(false));
    }

    /// <summary>
    /// Streams Form AI authoring events as Server-Sent Events.
    /// </summary>
    [HttpPost("forms/{formDefinitionId:guid}/chat/stream", Name = "postFormAiChatStream")]
    [EndpointDescription(
        "Non-resource SSE stream (text/event-stream). Progressive Form AI "
        + "authoring events for the designer UI. Kept outside JSON:API by "
        + "design: media type and incremental event framing are incompatible "
        + "with application/vnd.api+json resource documents. "
        + "Request body Content-Type: application/json.")]
    [Consumes("application/json")]
    [Produces("text/event-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task ChatStreamAsync(
        Guid formDefinitionId,
        CancellationToken cancellationToken)
    {
        FormAiChatRequest request = await ReadBodyAsync<FormAiChatRequest>(
            cancellationToken).ConfigureAwait(false);
        string code = await RequireFormCodeAsync(formDefinitionId, cancellationToken)
            .ConfigureAwait(false);
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        await formAiService.ChatStreamAsync(
            code,
            request,
            Response.Body,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> ReadBodyAsync<T>(CancellationToken cancellationToken)
    {
        T? value = await JsonSerializer
            .DeserializeAsync<T>(Request.Body, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return value
            ?? throw new Application.ValidationException(
                "Request body is required.");
    }

    private async Task<string> RequireFormCodeAsync(
        Guid formDefinitionId,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        FormDefinition? definition = await dbContext.FormDefinitions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == formDefinitionId,
                cancellationToken)
            .ConfigureAwait(false);
        if (definition is null
            || definition.HospitalId != hospitalContext.HospitalId)
        {
            throw new Application.NotFoundException(
                $"Form definition '{formDefinitionId}' was not found.");
        }

        return definition.Code;
    }
}
