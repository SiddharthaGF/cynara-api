using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Application.Modules.Patients;

/// <summary>
/// Composition extensions that wire the patient registry application
/// services. The repository is registered in
/// <c>Cynara.Infrastructure.Modules.Patients</c> so the application layer
/// remains persistence-agnostic.
/// </summary>
public static class PatientsModule
{
    public static IServiceCollection AddPatientsModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<IPatientService, PatientService>();
        return services;
    }
}
