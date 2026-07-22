using System.Text.Json;

using Cynara.Application;
using Cynara.Application.Components;
using Cynara.Application.Forms;
using Cynara.Application.Modules.Components;

using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Infrastructure.Modules.Preview;

public static class PreviewDemoSeeder
{
    public static async Task SeedPreviewDemoAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        AsyncServiceScope scope = services.CreateAsyncScope();
        try
        {
            IComponentQueryService componentQueries = scope.ServiceProvider
                .GetRequiredService<IComponentQueryService>();
            IComponentLifecycleService componentLifecycle = scope.ServiceProvider
                .GetRequiredService<IComponentLifecycleService>();
            IFormService forms = scope.ServiceProvider.GetRequiredService<IFormService>();

            const string actor = "preview-seed";
            const string componentCode = "patient-demographics";
            const string formCode = "demo-showcase";

            try
            {
                _ = await componentQueries.GetSummaryAsync(componentCode, cancellationToken).ConfigureAwait(false);
            }
            catch (NotFoundException)
            {
                ComponentSummaryDto summary = await componentLifecycle.CreateAsync(
                    new CreateComponentRequest(
                        componentCode,
                        "Patient demographics",
                        LoadJson("patient-demographics-clinical.json"),
                        LoadJson("patient-demographics-ui.json")),
                    actor,
                    cancellationToken).ConfigureAwait(false);
                ComponentVersionDto draft = await componentQueries.GetDraftAsync(
                    summary.Code,
                    cancellationToken).ConfigureAwait(false);
                _ = await componentLifecycle.PublishDraftAsync(
                    componentCode,
                    new PublishComponentDraftRequest(draft.RowVersion),
                    actor,
                    cancellationToken).ConfigureAwait(false);
            }

            IReadOnlyList<FormSummaryDto> existingForms = await forms.ListAsync(cancellationToken).ConfigureAwait(false);
            if (existingForms.All(item => item.Code != formCode))
            {
                _ = await forms.CreateAsync(
                    new CreateFormRequest(
                        formCode,
                        "Clinical showcase (preview)",
                        LoadJson("demo-showcase-clinical.json"),
                        LoadJson("demo-showcase-ui.json"),
                        LoadJson("demo-showcase-rules.json")),
                    actor,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string LoadJson(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "SeedData", fileName);
        return JsonSerializer.Serialize(
            JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path)));
    }
}
