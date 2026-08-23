using Cynara.Application.Modules.Patients.Persistence;

using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Infrastructure.Modules.Patients;

/// <summary>
/// Composition extensions wiring the patient registry persistence services;
/// only infrastructure registers the EF Core repository implementation.
/// </summary>
public static class PatientsPersistenceModule
{
    public static IServiceCollection AddPatientsPersistenceModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<IPatientRepository, PatientRepository>();
        return services;
    }
}
