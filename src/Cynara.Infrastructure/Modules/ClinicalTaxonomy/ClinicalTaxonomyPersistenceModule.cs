using Cynara.Application.Modules.ClinicalTaxonomy.Persistence;

using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Infrastructure.Modules.ClinicalTaxonomy;

public static class ClinicalTaxonomyPersistenceModule
{
    public static IServiceCollection AddClinicalTaxonomyPersistenceModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<IClinicalTaxonomyRepository, ClinicalTaxonomyRepository>();
        return services;
    }
}
