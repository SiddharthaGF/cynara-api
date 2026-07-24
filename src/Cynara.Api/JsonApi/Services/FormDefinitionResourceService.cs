using Cynara.Api.Common.ActorContext;
using Cynara.Application.Forms;
using Cynara.Application.Modules.Hospitals;
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
/// Resource reads enforce tenant scope by raising 404 for cross-tenant
/// identifiers, preventing one hospital from probing another hospital's
/// catalog.
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
    IHospitalContext hospitalContext,
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
        hospitalContext.RequireResolved();

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
                item => item.Code == created.Code
                    && item.HospitalId == hospitalContext.HospitalId,
                cancellationToken)
            .ConfigureAwait(false);

        return await GetAsync(definition.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    public override async Task<FormDefinition> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();

        var ownership = await dbContext.FormDefinitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(item => new { item.Id, item.HospitalId, item.DeletedAt })
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (ownership is null || ownership.HospitalId != hospitalContext.HospitalId)
        {
            throw new Application.NotFoundException(
                $"Form definition '{id}' was not found.");
        }

        if (ownership.DeletedAt is not null)
        {
            throw new Application.NotFoundException(
                $"Form definition '{id}' was not found.");
        }

        FormDefinition? definition = await base.GetAsync(id, cancellationToken)
            .ConfigureAwait(false);
        return definition!;
    }

    public override async Task<object?> GetSecondaryAsync(
        Guid id,
        string relationshipName,
        CancellationToken cancellationToken)
    {
        if (await base.GetSecondaryAsync(
                id,
                relationshipName,
                cancellationToken)
            .ConfigureAwait(false) is not FormDefinition definition)
        {
            return null;
        }

        hospitalContext.RequireResolved();
        if (definition.HospitalId != hospitalContext.HospitalId)
        {
            return null;
        }

        return definition;
    }

    public override async Task<IReadOnlyCollection<FormDefinition>> GetAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<FormDefinition> definitions = await base
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);
        hospitalContext.RequireResolved();
        return [.. definitions.Where(item => item.HospitalId == hospitalContext.HospitalId)];
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
