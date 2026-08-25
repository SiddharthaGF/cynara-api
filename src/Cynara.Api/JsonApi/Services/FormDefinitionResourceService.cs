using Cynara.Application.Forms;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Forms;

using JsonApiDotNetCore.Resources;

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
    IFormService formService,
    JsonApiResourceDeps deps,
    IResourceChangeTracker<FormDefinition> resourceChangeTracker)
    : TenantScopedResourceService<FormDefinition, Guid>(
        deps,
        resourceChangeTracker)
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
        await RequireCapabilityAsync(
            CapabilityCodes.CatalogWrite,
            cancellationToken).ConfigureAwait(false);

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
            ActorId,
            cancellationToken).ConfigureAwait(false);

        FormDefinition definition = await DbContext.FormDefinitions
            .AsNoTracking()
            .SingleAsync(
                item => item.Code == created.Code
                    && item.HospitalId == HospitalId,
                cancellationToken)
            .ConfigureAwait(false);

        return await GetAsync(definition.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    public override async Task<FormDefinition> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await RequireCapabilityAsync(
            CapabilityCodes.CatalogRead,
            cancellationToken).ConfigureAwait(false);

        TenantOwnership? ownership = await DbContext.FormDefinitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new TenantOwnership(item.HospitalId, item.DeletedAt))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        EnsureTenantOwnedActive(ownership, id, "Form definition");

        return await base.GetAsync(id, cancellationToken)
            .ConfigureAwait(false);
    }

    public override async Task<IReadOnlyCollection<FormDefinition>> GetAsync(
        CancellationToken cancellationToken)
    {
        await RequireCapabilityAsync(
            CapabilityCodes.CatalogRead,
            cancellationToken).ConfigureAwait(false);

        return await base
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public override async Task<object?> GetSecondaryAsync(
        Guid id,
        string relationshipName,
        CancellationToken cancellationToken)
    {
        await RequireCapabilityAsync(
            CapabilityCodes.CatalogRead,
            cancellationToken).ConfigureAwait(false);

        if (await base.GetSecondaryAsync(
                id,
                relationshipName,
                cancellationToken)
            .ConfigureAwait(false) is not FormDefinition definition)
        {
            return null;
        }

        HospitalContext.RequireResolved();
        if (definition.HospitalId != HospitalId)
        {
            return null;
        }

        return definition;
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
