using Cynara.Api.Common.ActorContext;
using Cynara.Application.Forms;
using Cynara.Domain.Forms;
using Cynara.Infrastructure.Persistence;

using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Middleware;
using JsonApiDotNetCore.Queries;
using JsonApiDotNetCore.Repositories;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Services;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Api.JsonApi.Services;

/// <summary>
/// Creates form definitions through <see cref="IFormService"/> so draft
/// seeding, validation, and audit stay in the application layer.
/// </summary>
public sealed class FormDefinitionResourceService(
    IResourceRepositoryAccessor repositoryAccessor,
    IQueryLayerComposer queryLayerComposer,
    IPaginationContext paginationContext,
    IJsonApiOptions options,
    ILoggerFactory loggerFactory,
    IJsonApiRequest request,
    IResourceChangeTracker<FormDefinition> resourceChangeTracker,
    IResourceDefinitionAccessor resourceDefinitionAccessor,
    IFormService formService,
    IHttpContextAccessor httpContextAccessor,
    CynaraDbContext dbContext)
    : JsonApiResourceService<FormDefinition, Guid>(
        repositoryAccessor,
        queryLayerComposer,
        paginationContext,
        options,
        loggerFactory,
        request,
        resourceChangeTracker,
        resourceDefinitionAccessor)
{
    private const string MinimalClinicalSchemaJson =
        /*lang=json,strict*/ """
            {
              "schemaVersion": "1.0.0",
              "fields": [
                {
                  "id": "placeholder",
                  "code": "form.placeholder",
                  "type": "text"
                }
              ]
            }
            """;

    public override async Task<FormDefinition?> CreateAsync(
        FormDefinition resource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);

        string clinical = string.IsNullOrWhiteSpace(
            resource.InitialClinicalSchemaJson)
            ? MinimalClinicalSchemaJson
            : resource.InitialClinicalSchemaJson;

        FormSummaryDto created = await formService.CreateAsync(
            new CreateFormRequest(
                resource.Code,
                resource.Name,
                clinical,
                resource.InitialUiSchemaJson,
                resource.InitialRulesSchemaJson),
            httpContextAccessor.HttpContext?.GetActorId(),
            cancellationToken).ConfigureAwait(false);

        FormDefinition definition = await dbContext.FormDefinitions
            .AsNoTracking()
            .SingleAsync(
                item => item.Code == created.Code,
                cancellationToken)
            .ConfigureAwait(false);

        return await GetAsync(definition.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    public override Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        throw new Application.InvalidStateException(
            "Use DELETE /api/formDefinitions/{id}/soft-delete-draft to "
            + "soft-delete a form definition.");
    }
}
