using System.Text.Json;

using Cynara.Application.Modules.FormAi;
using Cynara.Domain.Forms;
using Cynara.Infrastructure.Persistence;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cynara.Api.JsonApi.Controllers;

/// <summary>
/// Non-CRUD Form AI endpoints (status, rich settings view, chat, SSE stream).
/// Settings secrets are managed via <c>/api/aiProviderSettings</c>.
/// Bodies are read with System.Text.Json to avoid JsonApiDotNetCore
/// ModelMetadataKind.Constructor failures on positional records.
/// </summary>
[ApiController]
[Route("api/ai")]
[Tags("Form AI")]
public sealed class FormAiController(
    IFormAiService formAiService,
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
        "Reports Form AI configuration status for the active provider settings "
        + "and environment fallbacks. Never returns the raw API key.")]
    [ProducesResponseType(typeof(FormAiStatusResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<FormAiStatusResponse>> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        return Ok(await formAiService.GetStatusAsync(cancellationToken)
            .ConfigureAwait(false));
    }

    /// <summary>
    /// Returns the public settings view including masked key and suggestions.
    /// </summary>
    [HttpGet("settings", Name = "getFormAiSettings")]
    [EndpointDescription(
        "Returns Form AI settings for the admin UI, including a masked API key "
        + "indicator and endpoint suggestions.")]
    [ProducesResponseType(typeof(FormAiSettingsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<FormAiSettingsResponse>> GetSettingsAsync(
        CancellationToken cancellationToken)
    {
        return Ok(await formAiService.GetSettingsAsync(cancellationToken)
            .ConfigureAwait(false));
    }

    /// <summary>
    /// Updates AI settings (including optional API key rotation).
    /// Prefer JSON:API PATCH on aiProviderSettings when only resource fields change.
    /// </summary>
    [HttpPut("settings", Name = "putFormAiSettings")]
    [EndpointDescription(
        "Upserts Form AI settings. Use clearApiKey to remove a stored secret. "
        + "Does not echo the raw API key. Content-Type: application/json.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(FormAiSettingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<FormAiSettingsResponse>> PutSettingsAsync(
        CancellationToken cancellationToken)
    {
        FormAiSettingsUpdateRequest request = await ReadBodyAsync<FormAiSettingsUpdateRequest>(
            cancellationToken).ConfigureAwait(false);
        return Ok(await formAiService.UpdateSettingsAsync(request, cancellationToken)
            .ConfigureAwait(false));
    }

    /// <summary>
    /// Runs a non-streaming AI authoring turn against a form's editable draft.
    /// </summary>
    [HttpPost("forms/{formDefinitionId:guid}/chat", Name = "postFormAiChat")]
    [EndpointDescription(
        "Invokes Form AI chat against the editable draft of the given form "
        + "definition. Returns proposed clinical/UI/rules schema updates. "
        + "Content-Type: application/json.")]
    [Consumes("application/json")]
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
        "Streams Form AI chat events (text/event-stream) for progressive UI "
        + "updates while drafting schemas. Content-Type: application/json.")]
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
        FormDefinition? definition = await dbContext.FormDefinitions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == formDefinitionId,
                cancellationToken)
            .ConfigureAwait(false);
        return definition?.Code
            ?? throw new Application.NotFoundException(
                $"Form definition '{formDefinitionId}' was not found.");
    }
}
