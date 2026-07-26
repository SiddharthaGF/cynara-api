using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Application.Modules.ClinicalTaxonomy;

public static class ClinicalTaxonomyModule
{
    public static IServiceCollection AddClinicalTaxonomyModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<IClinicalTaxonomyService, ClinicalTaxonomyService>();
        return services;
    }
}
