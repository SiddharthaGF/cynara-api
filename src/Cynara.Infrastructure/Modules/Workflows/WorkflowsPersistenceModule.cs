using Cynara.Application.Modules.Workflows.Persistence;

using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Infrastructure.Modules.Workflows;

public static class WorkflowsPersistenceModule
{
    public static IServiceCollection AddWorkflowsPersistenceModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<IWorkflowRepository, WorkflowRepository>();
        return services;
    }
}
