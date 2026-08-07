using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Application.Modules.Tasks;

public static class TasksModule
{
    public static IServiceCollection AddTasksModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<ITaskService, TaskService>();
        return services;
    }
}
