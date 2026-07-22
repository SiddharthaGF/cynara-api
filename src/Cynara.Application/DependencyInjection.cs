using Cynara.Application.Modules.Audit;
using Cynara.Application.Modules.Components;
using Cynara.Application.Modules.FormAi;
using Cynara.Application.Modules.FormResponses;
using Cynara.Application.Modules.Forms;

using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddCynaraApplication(
        this IServiceCollection services)
    {
        _ = services.AddAuditModule();
        _ = services.AddComponentsModule();
        _ = services.AddFormsModule();
        _ = services.AddFormResponsesModule();
        _ = services.AddFormAiModule();

        return services;
    }
}
