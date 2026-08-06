using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Application.Modules.Workflows;

public static class WorkflowsModule
{
    public static IServiceCollection AddWorkflowsModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<IWorkflowLifecycleService, WorkflowLifecycleService>();
        _ = services.AddScoped<IWorkflowQueryService, WorkflowQueriesService>();
        return services;
    }
}
