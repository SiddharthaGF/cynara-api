using Cynara.Application.Modules.Tasks.Persistence;

using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Infrastructure.Modules.Tasks;

public static class TasksPersistenceModule
{
    public static IServiceCollection AddTasksPersistenceModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<ITaskRepository, TaskRepository>();
        return services;
    }
}
