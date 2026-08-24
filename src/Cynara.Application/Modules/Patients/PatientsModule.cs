namespace Cynara.Application.Modules.Patients;

/// <summary>
/// Composition extensions wiring the patient registry application services;
/// the repository registers in Infrastructure so Application stays
/// persistence-agnostic.
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
