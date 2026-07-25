using System.Text.Json;

using Cynara.Application;
using Cynara.Application.Components;
using Cynara.Application.Forms;
using Cynara.Application.Modules.Components;
using Cynara.Application.Modules.Hospitals;
using Cynara.Domain.Hospitals;
using Cynara.Infrastructure.Modules.Hospitals;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Infrastructure.Modules.Preview;

public static class DemoShowcaseSeeder
{
    public const string ComponentCode = "patient-demographics";
    public const string FormCode = "demo-showcase";

    private const string ActorId = "demo-seed";

    public static async Task SeedDemoShowcaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        AsyncServiceScope scope = services.CreateAsyncScope();
        try
        {
            CynaraDbContext dbContext = scope.ServiceProvider
                .GetRequiredService<CynaraDbContext>();
            Hospital hospital = await ResolveHospitalAsync(
                    dbContext,
                    cancellationToken)
                .ConfigureAwait(false);
            HospitalContext hospitalContext = scope.ServiceProvider
                .GetRequiredService<HospitalContext>();
            hospitalContext.SetWorkspace(hospital.Id, hospital.Code, hospital.Name);

            IComponentQueryService componentQueries = scope.ServiceProvider
                .GetRequiredService<IComponentQueryService>();
            IComponentLifecycleService componentLifecycle = scope.ServiceProvider
                .GetRequiredService<IComponentLifecycleService>();
            IFormService forms = scope.ServiceProvider
                .GetRequiredService<IFormService>();

            await EnsureComponentAsync(
                    componentQueries,
                    componentLifecycle,
                    cancellationToken)
                .ConfigureAwait(false);
            await UpsertFormAsync(forms, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    public static Task SeedPreviewDemoAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        return services.SeedDemoShowcaseAsync(cancellationToken);
    }

    private static async Task<Hospital> ResolveHospitalAsync(
        CynaraDbContext dbContext,
        CancellationToken cancellationToken)
    {
        Hospital? hospital = await dbContext.Hospitals
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return hospital
            ?? await HospitalBootstrap.EnsureBootstrapHospitalAsync(
                dbContext,
                new HospitalBootstrapOptions
                {
                    BootstrapCode = HospitalBootstrap.DefaultBootstrapCode,
                    BootstrapName = HospitalBootstrap.DefaultBootstrapName,
                    HeaderName = HospitalBootstrapOptions.DefaultHeaderName,
                    AllowAutoBootstrap = true,
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsureComponentAsync(
        IComponentQueryService componentQueries,
        IComponentLifecycleService componentLifecycle,
        CancellationToken cancellationToken)
    {
        bool exists;
        try
        {
            _ = await componentQueries
                .GetSummaryAsync(ComponentCode, cancellationToken)
                .ConfigureAwait(false);
            exists = true;
        }
        catch (NotFoundException)
        {
            exists = false;
        }

        if (exists)
        {
            return;
        }

        ComponentSummaryDto summary = await componentLifecycle.CreateAsync(
            new CreateComponentRequest(
                ComponentCode,
                "Patient demographics",
                LoadJson("patient-demographics-clinical.json"),
                LoadJson("patient-demographics-ui.json")),
            ActorId,
            cancellationToken).ConfigureAwait(false);
        ComponentVersionDto draft = await componentQueries.GetDraftAsync(
            summary.Code,
            cancellationToken).ConfigureAwait(false);
        _ = await componentLifecycle.PublishDraftAsync(
            ComponentCode,
            new PublishComponentDraftRequest(draft.RowVersion),
            ActorId,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertFormAsync(
        IFormService forms,
        CancellationToken cancellationToken)
    {
        string clinical = LoadJson("demo-showcase-clinical.json");
        string ui = LoadJson("demo-showcase-ui.json");
        string rules = LoadJson("demo-showcase-rules.json");

        IReadOnlyList<FormSummaryDto> existingForms = await forms
            .ListAsync(cancellationToken)
            .ConfigureAwait(false);
        FormSummaryDto? existing = existingForms.FirstOrDefault(
            item => string.Equals(item.Code, FormCode, StringComparison.Ordinal));

        if (existing is null)
        {
            _ = await forms.CreateAsync(
                new CreateFormRequest(
                    FormCode,
                    "Clinical showcase (preview)",
                    clinical,
                    ui,
                    rules),
                ActorId,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (string.Equals(existing.EditableStatus, "review", StringComparison.Ordinal))
        {
            return;
        }

        FormVersionDto draft;
        if (existing.EditableVersionId is null)
        {
            draft = await forms
                .CreateDraftFromLatestAsync(FormCode, ActorId, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            draft = await forms
                .GetEditableVersionAsync(FormCode, cancellationToken)
                .ConfigureAwait(false);
        }

        _ = await forms.UpdateDraftAsync(
            FormCode,
            new UpdateFormDraftRequest(
                clinical,
                ui,
                rules,
                draft.RowVersion),
            ActorId,
            cancellationToken).ConfigureAwait(false);
    }

    private static string LoadJson(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "SeedData", fileName);
        return JsonSerializer.Serialize(
            JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path)));
    }
}
