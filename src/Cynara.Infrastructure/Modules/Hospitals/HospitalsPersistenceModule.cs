using Cynara.Application.Modules.Hospitals;

namespace Cynara.Infrastructure.Modules.Hospitals;

public static class HospitalsPersistenceModule
{
    public static IServiceCollection AddHospitalsPersistenceModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<IHospitalRepository, HospitalRepository>();
        return services;
    }
}
