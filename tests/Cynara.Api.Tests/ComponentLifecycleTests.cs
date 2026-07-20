using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Cynara.Application.Components;
using Cynara.Domain.Audit;
using Cynara.Infrastructure.Persistence;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Cynara.Api.Tests;

public class ComponentLifecycleTests : IDisposable
{
    public ComponentLifecycleTests()
    {
        Client = Factory.CreateClient();
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
    }

    [Fact]
    public async Task ComponentLifecycle_CreateDraftPublishRetireAndResolveHistory()
    {
        var createRequest = new CreateComponentRequest(
            "patient-demographics",
            "Patient demographics",
            MinimalClinicalSchema("patient-name", "patient.name"),
            MinimalUiSchema("patient-name"));

        using HttpResponseMessage createResponse = await Client.PostAsJsonAsync("/api/components", createRequest);
        await AssertStatusAsync(createResponse, HttpStatusCode.Created);

        ComponentSummaryDto created = (await createResponse.Content.ReadFromJsonAsync<ComponentSummaryDto>())!;
        Assert.Equal("patient-demographics", created.Code);
        Assert.NotNull(created.DraftVersionId);

        ComponentVersionDto draft = await GetDraftAsync("patient-demographics");
        Assert.Equal("draft", draft.Status);
        Assert.Equal(0u, draft.RowVersion);

        string updatedClinical = MinimalClinicalSchema("patient-full-name", "patient.full-name");
        var updateRequest = new UpdateComponentDraftRequest(updatedClinical, draft.UiSchemaJson, draft.RowVersion);
        using HttpResponseMessage updateResponse = await Client.PutAsJsonAsync("/api/components/patient-demographics/draft", updateRequest);
        await AssertStatusAsync(updateResponse, HttpStatusCode.OK);

        ComponentVersionDto updatedDraft = (await updateResponse.Content.ReadFromJsonAsync<ComponentVersionDto>())!;
        Assert.Equal(1u, updatedDraft.RowVersion);
        Assert.Contains("patient-full-name", updatedDraft.ClinicalSchemaJson, StringComparison.Ordinal);

        var publishRequest = new PublishComponentDraftRequest(updatedDraft.RowVersion);
        using HttpResponseMessage publishResponse = await Client.PostAsJsonAsync(
            "/api/components/patient-demographics/draft/publish",
            publishRequest);
        await AssertStatusAsync(publishResponse, HttpStatusCode.OK);

        ComponentVersionDto published = (await publishResponse.Content.ReadFromJsonAsync<ComponentVersionDto>())!;
        Assert.Equal("published", published.Status);
        Assert.Equal("1.0.0", published.Version);
        Assert.False(string.IsNullOrWhiteSpace(published.ContentHash));

        ComponentVersionDto resolved = await GetVersionAsync("patient-demographics", "1.0.0");
        Assert.Equal(published.Id, resolved.Id);
        Assert.Equal(published.ClinicalSchemaJson, resolved.ClinicalSchemaJson);

        using HttpResponseMessage retireResponse = await Client.PostAsync(
            "/api/components/patient-demographics/versions/1.0.0/retire",
            content: null);
        await AssertStatusAsync(retireResponse, HttpStatusCode.OK);

        ComponentVersionDto retired = (await retireResponse.Content.ReadFromJsonAsync<ComponentVersionDto>())!;
        Assert.Equal("retired", retired.Status);

        ComponentVersionDto stillResolvable = await GetVersionAsync("patient-demographics", "1.0.0");
        Assert.Equal("retired", stillResolvable.Status);

        await AssertAuditEventsRecordedAsync(
            published.Id,
            "component.version.published",
            "component.version.retired");
    }

    [Fact]
    public async Task SoftDeleteDraft_RemovesUnusedComponent()
    {
        var createRequest = new CreateComponentRequest(
            "unused-section",
            "Unused section",
            MinimalClinicalSchema("section-notes", "section.notes"),
            null);

        using HttpResponseMessage createResponse = await Client.PostAsJsonAsync("/api/components", createRequest);
        await AssertStatusAsync(createResponse, HttpStatusCode.Created);

        using HttpResponseMessage deleteResponse = await Client.DeleteAsync("/api/components/unused-section/draft");
        await AssertStatusAsync(deleteResponse, HttpStatusCode.NoContent);

        using HttpResponseMessage getResponse = await Client.GetAsync("/api/components/unused-section");
        await AssertStatusAsync(getResponse, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SoftDeleteDraft_AllowsDeleteAfterPublishedVersionIsRetired()
    {
        var createRequest = new CreateComponentRequest(
            "retired-only",
            "Retired only",
            MinimalClinicalSchema("notes", "section.notes"),
            null);

        using HttpResponseMessage createResponse = await Client.PostAsJsonAsync("/api/components", createRequest);
        await AssertStatusAsync(createResponse, HttpStatusCode.Created);

        ComponentVersionDto draft = await GetDraftAsync("retired-only");
        var publishRequest = new PublishComponentDraftRequest(draft.RowVersion);
        using HttpResponseMessage publishResponse = await Client.PostAsJsonAsync(
            "/api/components/retired-only/draft/publish",
            publishRequest);
        await AssertStatusAsync(publishResponse, HttpStatusCode.OK);

        using HttpResponseMessage retireResponse = await Client.PostAsync(
            "/api/components/retired-only/versions/1.0.0/retire",
            content: null);
        await AssertStatusAsync(retireResponse, HttpStatusCode.OK);

        using HttpResponseMessage createDraftResponse = await Client.PostAsync(
            "/api/components/retired-only/draft",
            content: null);
        await AssertStatusAsync(createDraftResponse, HttpStatusCode.Created);

        using HttpResponseMessage deleteResponse = await Client.DeleteAsync("/api/components/retired-only/draft");
        await AssertStatusAsync(deleteResponse, HttpStatusCode.NoContent);

        using HttpResponseMessage getResponse = await Client.GetAsync("/api/components/retired-only");
        await AssertStatusAsync(getResponse, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PublishDraft_IsImmutableAfterPublish()
    {
        var createRequest = new CreateComponentRequest(
            "vitals-panel",
            "Vitals panel",
            MinimalClinicalSchema("vitals-systolic", "vitals.systolic"),
            MinimalUiSchema("vitals-systolic"));

        using HttpResponseMessage createResponse = await Client.PostAsJsonAsync("/api/components", createRequest);
        await AssertStatusAsync(createResponse, HttpStatusCode.Created);

        ComponentVersionDto draft = await GetDraftAsync("vitals-panel");
        var publishRequest = new PublishComponentDraftRequest(draft.RowVersion);
        using HttpResponseMessage publishResponse = await Client.PostAsJsonAsync(
            "/api/components/vitals-panel/draft/publish",
            publishRequest);
        await AssertStatusAsync(publishResponse, HttpStatusCode.OK);

        ComponentVersionDto published = (await publishResponse.Content.ReadFromJsonAsync<ComponentVersionDto>())!;
        var staleUpdate = new UpdateComponentDraftRequest(
            MinimalClinicalSchema("vitals-diastolic", "vitals.diastolic"),
            published.UiSchemaJson,
            published.RowVersion);

        using HttpResponseMessage updateResponse = await Client.PutAsJsonAsync("/api/components/vitals-panel/draft", staleUpdate);
        await AssertStatusAsync(updateResponse, HttpStatusCode.NotFound);
    }

    private HttpClient Client { get; }

    private ComponentWebApplicationFactory Factory { get; } = new();

    private async Task<ComponentVersionDto> GetDraftAsync(string code)
    {
        using HttpResponseMessage response = await Client.GetAsync($"/api/components/{code}/draft");
        await AssertStatusAsync(response, HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ComponentVersionDto>())!;
    }

    private async Task<ComponentVersionDto> GetVersionAsync(string code, string version)
    {
        using HttpResponseMessage response = await Client.GetAsync($"/api/components/{code}/versions/{version}");
        await AssertStatusAsync(response, HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ComponentVersionDto>())!;
    }

    private async Task AssertAuditEventsRecordedAsync(Guid resourceId, params string[] actions)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider.GetRequiredService<CynaraDbContext>();
        List<AuditEvent> events = [.. (await dbContext.AuditEvents
            .Where(item => item.ResourceId == resourceId)
            .ToListAsync())
            .OrderBy(item => item.OccurredAt)];

        foreach (string action in actions)
        {
            Assert.Contains(events, item => item.Action == action);
        }
    }

    private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode == expected)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync();
        Assert.Fail($"Expected {(int)expected} {expected}, got {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
    }

    private static string MinimalClinicalSchema(string id, string code)
    {
        return JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0.0",
            fields = new[]
            {
                new
                {
                    id,
                    code,
                    type = "text",
                    maxLength = 500,
                },
            },
        });
    }

    private static string MinimalUiSchema(string fieldId)
    {
        return JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0.0",
            clinicalSchemaVersion = "1.0.0",
            fields = new Dictionary<string, object>
            {
                [fieldId] = new
                {
                    label = "Field label",
                    widget = "text-input",
                },
            },
        });
    }
}

public sealed class ComponentWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"cynara-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddJsonFile(
                Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
                optional: false,
                reloadOnChange: false);
        });

        builder.ConfigureServices(services =>
        {
            ServiceDescriptor? dbContextDescriptor = services.SingleOrDefault(
                descriptor => descriptor.ServiceType == typeof(DbContextOptions<CynaraDbContext>));
            if (dbContextDescriptor is not null)
            {
                services.Remove(dbContextDescriptor);
            }

            services.RemoveAll<CynaraDbContext>();

            string connectionString = $"Data Source={_databasePath}";
            services.AddDbContext<CynaraDbContext>(options => options.UseSqlite(connectionString));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
