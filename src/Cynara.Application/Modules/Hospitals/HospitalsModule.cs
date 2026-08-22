using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Application.Modules.Hospitals;

public static class HospitalsModule
{
    public static IServiceCollection AddHospitalsModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<HospitalContext>();
        _ = services.AddScoped<IHospitalContext>(
            provider => provider.GetRequiredService<HospitalContext>());
        _ = services.AddScoped<IHospitalWorkspaceService, HospitalWorkspaceService>();
        _ = services.AddScoped<HospitalMembershipService>();
        return services;
    }
}
