using Cynara.Application.Forms;

namespace Cynara.Application.Modules.Forms;

public static class FormsModule
{
    public static IServiceCollection AddFormsModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<IFormCompiler, FormCompiler>();
        _ = services.AddScoped<IFormReviewService, FormReviewService>();
        _ = services.AddSingleton<IFormRuleEngine, FormRuleEngine>();
        _ = services.AddScoped<IFormService, FormService>();
        return services;
    }
}
