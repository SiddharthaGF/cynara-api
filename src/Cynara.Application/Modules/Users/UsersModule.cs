namespace Cynara.Application.Modules.Users;

/// <summary>
/// Composition extensions wiring the user directory application services;
/// the read-only port registers in Infrastructure beside the other
/// cross-context identity readers so Application stays persistence-agnostic.
/// </summary>
public static class UsersModule
{
    public static IServiceCollection AddUsersModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<IUserDirectoryService, UserDirectoryService>();
        return services;
    }
}
