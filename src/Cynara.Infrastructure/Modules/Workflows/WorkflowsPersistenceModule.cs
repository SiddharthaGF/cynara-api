using Cynara.Application.Modules.Workflows.Persistence;

namespace Cynara.Infrastructure.Modules.Workflows;

public static class WorkflowsPersistenceModule
{
    public static IServiceCollection AddWorkflowsPersistenceModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<IWorkflowRepository, WorkflowRepository>();
        _ = services.AddScoped<IPipelineRepository, PipelineRepository>();
        return services;
    }
}
