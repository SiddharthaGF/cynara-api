using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Application.Modules.Users;

/// <summary>
/// Composition extensions that wire the user directory application services.
/// The read-only persistence port implementation is registered in
/// <c>Cynara.Infrastructure.Modules.Identity</c> beside the other
/// cross-context identity readers so the application layer stays
/// persistence-agnostic.
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
