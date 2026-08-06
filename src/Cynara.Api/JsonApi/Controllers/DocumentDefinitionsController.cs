using Cynara.Api.CapabilityAuthorization;
using Cynara.Api.Common.ActorContext;
using Cynara.Application.Modules.Documents;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Documents;

using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Services;

using Microsoft.AspNetCore.Mvc;

namespace Cynara.Api.JsonApi.Controllers;

/// <summary>
/// JSON:API controller for document catalog entries plus a retire
/// action. The catalog lifecycle (create, update, query) flows through
/// the resource service; this controller only adds the domain-specific
/// retire action that mutates the lifecycle status.
/// </summary>
[Route("api/documentDefinitions")]
public sealed class DocumentDefinitionsController(
    IJsonApiOptions options,
    IResourceGraph resourceGraph,
    ILoggerFactory loggerFactory,
    IResourceService<DocumentDefinition, Guid> resourceService,
    IDocumentCatalogService catalog,
    IHttpContextAccessor httpContextAccessor)
    : JsonApiController<DocumentDefinition, Guid>(options, resourceGraph, loggerFactory, resourceService)
{
    /// <summary>
    /// Retires an existing document catalog entry. The pinned
    /// <c>FormVersionId</c> snapshot is preserved so historical documents
    /// remain resolvable.
    /// </summary>
    [HttpPost("{id:guid}/retire")]
    [RequireCapability(CapabilityCodes.CatalogWrite)]
    [EndpointDescription(
        "Retires a document catalog entry. Pass rowVersion as a query "
        + "parameter for optimistic concurrency.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RetireAsync(
        Guid id,
        [FromQuery] uint rowVersion,
        CancellationToken cancellationToken)
    {
        _ = await catalog
            .RetireAsync(
                id,
                new RetireDocumentDefinitionRequest(rowVersion),
                httpContextAccessor.HttpContext?.GetActorId(),
                cancellationToken)
            .ConfigureAwait(false);
        return await GetAsync(id, cancellationToken).ConfigureAwait(false);
    }
}
