using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Cynara.Application.Components;
using Cynara.Domain.Audit;
using Cynara.Infrastructure.Persistence;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Cynara.Api.Tests;

public sealed class ComponentLifecycleTests : IDisposable
{
    public ComponentLifecycleTests()
    {
        Client = Factory.CreateClient();
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ComponentLifecycle_CreateDraftPublishRetireAndResolveHistory()
    {
        var createRequest = new CreateComponentRequest(
            "patient-demographics",
            "Patient demographics",
            MinimalClinicalSchema("patient-name", "patient.name"),
            MinimalUiSchema("patient-name"));

        using HttpResponseMessage createResponse = await Client.PostAsJsonAsync("/api/components", createRequest).ConfigureAwait(false);
        await AssertStatusAsync(createResponse, HttpStatusCode.Created).ConfigureAwait(false);

        ComponentSummaryDto created = (await createResponse.Content.ReadFromJsonAsync<ComponentSummaryDto>().ConfigureAwait(false))!;
        Assert.Equal("patient-demographics", created.Code);
        Assert.NotNull(created.DraftVersionId);

        ComponentVersionDto draft = await GetDraftAsync("patient-demographics").ConfigureAwait(false);
        Assert.Equal("draft", draft.Status);
        Assert.Equal(0u, draft.RowVersion);

        string updatedClinical = MinimalClinicalSchema("patient-full-name", "patient.full-name");
        var updateRequest = new UpdateComponentDraftRequest(updatedClinical, draft.UiSchemaJson, draft.RowVersion);
        using HttpResponseMessage updateResponse = await Client.PutAsJsonAsync("/api/components/patient-demographics/draft", updateRequest).ConfigureAwait(false);
        await AssertStatusAsync(updateResponse, HttpStatusCode.OK).ConfigureAwait(false);

        ComponentVersionDto updatedDraft = (await updateResponse.Content.ReadFromJsonAsync<ComponentVersionDto>().ConfigureAwait(false))!;
        Assert.Equal(1u, updatedDraft.RowVersion);
        Assert.Contains("patient-full-name", updatedDraft.ClinicalSchemaJson, StringComparison.Ordinal);

        var publishRequest = new PublishComponentDraftRequest(updatedDraft.RowVersion);
        using HttpResponseMessage publishResponse = await Client.PostAsJsonAsync(
            "/api/components/patient-demographics/draft/publish",
            publishRequest).ConfigureAwait(false);
        await AssertStatusAsync(publishResponse, HttpStatusCode.OK).ConfigureAwait(false);

        ComponentVersionDto published = (await publishResponse.Content.ReadFromJsonAsync<ComponentVersionDto>().ConfigureAwait(false))!;
        Assert.Equal("published", published.Status);
        Assert.Equal("1.0.0", published.Version);
        Assert.False(string.IsNullOrWhiteSpace(published.ContentHash));

        ComponentVersionDto resolved = await GetVersionAsync("patient-demographics", "1.0.0").ConfigureAwait(false);
        Assert.Equal(published.Id, resolved.Id);
        Assert.Equal(published.ClinicalSchemaJson, resolved.ClinicalSchemaJson);

        using HttpResponseMessage retireResponse = await Client.PostAsync(
            new Uri("/api/components/patient-demographics/versions/1.0.0/retire", UriKind.Relative),
            content: null).ConfigureAwait(false);
        await AssertStatusAsync(retireResponse, HttpStatusCode.OK).ConfigureAwait(false);

        ComponentVersionDto retired = (await retireResponse.Content.ReadFromJsonAsync<ComponentVersionDto>().ConfigureAwait(false))!;
        Assert.Equal("retired", retired.Status);

        ComponentVersionDto stillResolvable = await GetVersionAsync("patient-demographics", "1.0.0").ConfigureAwait(false);
        Assert.Equal("retired", stillResolvable.Status);

        await AssertAuditEventsRecordedAsync(
            published.Id,
            "component.version.published",
            "component.version.retired").ConfigureAwait(false);
    }

    [Fact]
    public async Task SoftDeleteDraft_RemovesUnusedComponent()
    {
        var createRequest = new CreateComponentRequest(
            "unused-section",
            "Unused section",
            MinimalClinicalSchema("section-notes", "section.notes"),
UiSchemaJson: null);

        using HttpResponseMessage createResponse = await Client.PostAsJsonAsync("/api/components", createRequest).ConfigureAwait(false);
        await AssertStatusAsync(createResponse, HttpStatusCode.Created).ConfigureAwait(false);

        using HttpResponseMessage deleteResponse = await Client.DeleteAsync(new Uri("/api/components/unused-section/draft", UriKind.Relative)).ConfigureAwait(false);
        await AssertStatusAsync(deleteResponse, HttpStatusCode.NoContent).ConfigureAwait(false);

        using HttpResponseMessage getResponse = await Client.GetAsync(new Uri("/api/components/unused-section", UriKind.Relative)).ConfigureAwait(false);
        await AssertStatusAsync(getResponse, HttpStatusCode.NotFound).ConfigureAwait(false);
    }

    [Fact]
    public async Task SoftDeleteDraft_AllowsDeleteAfterPublishedVersionIsRetired()
    {
        var createRequest = new CreateComponentRequest(
            "retired-only",
            "Retired only",
            MinimalClinicalSchema("notes", "section.notes"),
UiSchemaJson: null);

        using HttpResponseMessage createResponse = await Client.PostAsJsonAsync("/api/components", createRequest).ConfigureAwait(false);
        await AssertStatusAsync(createResponse, HttpStatusCode.Created).ConfigureAwait(false);

        ComponentVersionDto draft = await GetDraftAsync("retired-only").ConfigureAwait(false);
        var publishRequest = new PublishComponentDraftRequest(draft.RowVersion);
        using HttpResponseMessage publishResponse = await Client.PostAsJsonAsync(
            "/api/components/retired-only/draft/publish",
            publishRequest).ConfigureAwait(false);
        await AssertStatusAsync(publishResponse, HttpStatusCode.OK).ConfigureAwait(false);

        using HttpResponseMessage retireResponse = await Client.PostAsync(
            new Uri("/api/components/retired-only/versions/1.0.0/retire", UriKind.Relative),
            content: null).ConfigureAwait(false);
        await AssertStatusAsync(retireResponse, HttpStatusCode.OK).ConfigureAwait(false);

        using HttpResponseMessage createDraftResponse = await Client.PostAsync(
            new Uri("/api/components/retired-only/draft", UriKind.Relative),
            content: null).ConfigureAwait(false);
        await AssertStatusAsync(createDraftResponse, HttpStatusCode.Created).ConfigureAwait(false);

        using HttpResponseMessage deleteResponse = await Client.DeleteAsync(new Uri("/api/components/retired-only/draft", UriKind.Relative)).ConfigureAwait(false);
        await AssertStatusAsync(deleteResponse, HttpStatusCode.NoContent).ConfigureAwait(false);

        using HttpResponseMessage getResponse = await Client.GetAsync(new Uri("/api/components/retired-only", UriKind.Relative)).ConfigureAwait(false);
        await AssertStatusAsync(getResponse, HttpStatusCode.NotFound).ConfigureAwait(false);
    }

    [Fact]
    public async Task PublishDraft_IsImmutableAfterPublish()
    {
        var createRequest = new CreateComponentRequest(
            "vitals-panel",
            "Vitals panel",
            MinimalClinicalSchema("vitals-systolic", "vitals.systolic"),
            MinimalUiSchema("vitals-systolic"));

        using HttpResponseMessage createResponse = await Client.PostAsJsonAsync("/api/components", createRequest).ConfigureAwait(false);
        await AssertStatusAsync(createResponse, HttpStatusCode.Created).ConfigureAwait(false);

        ComponentVersionDto draft = await GetDraftAsync("vitals-panel").ConfigureAwait(false);
        var publishRequest = new PublishComponentDraftRequest(draft.RowVersion);
        using HttpResponseMessage publishResponse = await Client.PostAsJsonAsync(
            new Uri("/api/components/vitals-panel/draft/publish", UriKind.Relative),
            publishRequest).ConfigureAwait(false);
        await AssertStatusAsync(publishResponse, HttpStatusCode.OK).ConfigureAwait(false);

        ComponentVersionDto published = (await publishResponse.Content.ReadFromJsonAsync<ComponentVersionDto>().ConfigureAwait(false))!;
        var staleUpdate = new UpdateComponentDraftRequest(
            MinimalClinicalSchema("vitals-diastolic", "vitals.diastolic"),
            published.UiSchemaJson,
            published.RowVersion);

        using HttpResponseMessage updateResponse = await Client.PutAsJsonAsync("/api/components/vitals-panel/draft", staleUpdate).ConfigureAwait(false);
        await AssertStatusAsync(updateResponse, HttpStatusCode.NotFound).ConfigureAwait(false);
    }

    private HttpClient Client { get; }

    private ComponentWebApplicationFactory Factory { get; } = new();

    private async Task<ComponentVersionDto> GetDraftAsync(string code)
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri($"/api/components/{code}/draft", UriKind.Relative)).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.OK).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<ComponentVersionDto>().ConfigureAwait(false))!;
    }

    private async Task<ComponentVersionDto> GetVersionAsync(string code, string version)
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri($"/api/components/{code}/versions/{version}", UriKind.Relative)).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.OK).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<ComponentVersionDto>().ConfigureAwait(false))!;
    }

    private async Task AssertAuditEventsRecordedAsync(Guid resourceId, params string[] actions)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider.GetRequiredService<CynaraDbContext>();
        List<AuditEvent> events = [.. (await dbContext.AuditEvents
            .Where(item => item.ResourceId == resourceId)
            .ToListAsync().ConfigureAwait(false))
            .OrderBy(item => item.OccurredAt)];

        foreach (string action in actions)
        {
            Assert.Contains(events, item => string.Equals(item.Action, action, StringComparison.Ordinal));
        }
    }

    private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode == expected)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.Fail(string.Create(CultureInfo.InvariantCulture, $"Expected {(int)expected} {expected}, got {(int)response.StatusCode} {response.StatusCode}. Body: {body}"));
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
(StringComparer.Ordinal)
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

internal sealed class ComponentWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection sharedConnection = new("Data Source=:memory:");

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        sharedConnection.Open();

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddJsonFile(
                Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
                optional: false,
                reloadOnChange: false);
        })
            .ConfigureServices(services =>
        {
            ServiceDescriptor? dbContextDescriptor = services.SingleOrDefault(
                descriptor => descriptor.ServiceType == typeof(DbContextOptions<CynaraDbContext>));
            if (dbContextDescriptor is not null)
            {
                services.Remove(dbContextDescriptor);
            }

            services.RemoveAll<CynaraDbContext>();

            services.AddDbContext<CynaraDbContext>(options => options.UseSqlite(sharedConnection));
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            sharedConnection.Dispose();
        }

        base.Dispose(disposing);
    }
}
