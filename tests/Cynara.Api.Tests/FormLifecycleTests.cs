using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Cynara.Application.Forms;
using Cynara.Domain.Audit;
using Cynara.Infrastructure.Persistence;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Xunit;

namespace Cynara.Api.Tests;

public sealed class FormLifecycleTests : IDisposable
{
    public FormLifecycleTests()
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
    public async Task FormLifecycle_CreateDraftPublishRetireAndResolveHistory()
    {
        var createRequest = new CreateFormRequest(
            "intake-assessment",
            "Intake assessment",
            MinimalClinicalSchema("patient-name", "patient.name"),
            MinimalUiSchema("patient-name"));

        using HttpResponseMessage createResponse = await Client.PostAsJsonAsync("/api/forms", createRequest).ConfigureAwait(false);
        await AssertStatusAsync(createResponse, HttpStatusCode.Created).ConfigureAwait(false);

        FormSummaryDto created = (await createResponse.Content.ReadFromJsonAsync<FormSummaryDto>().ConfigureAwait(false))!;
        Assert.Equal("intake-assessment", created.Code);
        Assert.NotNull(created.EditableVersionId);
        Assert.Equal("draft", created.EditableStatus);

        FormVersionDto draft = await GetEditableVersionAsync("intake-assessment").ConfigureAwait(false);
        Assert.Equal("draft", draft.Status);
        Assert.Equal(0u, draft.RowVersion);

        string updatedClinical = MinimalClinicalSchema("patient-full-name", "patient.full-name");
        var updateRequest = new UpdateFormDraftRequest(updatedClinical, draft.UiSchemaJson, draft.RulesSchemaJson, draft.RowVersion);
        using HttpResponseMessage updateResponse = await Client.PutAsJsonAsync("/api/forms/intake-assessment/draft", updateRequest).ConfigureAwait(false);
        await AssertStatusAsync(updateResponse, HttpStatusCode.OK).ConfigureAwait(false);

        FormVersionDto updatedDraft = (await updateResponse.Content.ReadFromJsonAsync<FormVersionDto>().ConfigureAwait(false))!;
        Assert.Equal(1u, updatedDraft.RowVersion);
        Assert.Contains("patient-full-name", updatedDraft.ClinicalSchemaJson, StringComparison.Ordinal);

        FormVersionDto inReview = await SubmitForReviewAsync("intake-assessment", updatedDraft.RowVersion).ConfigureAwait(false);
        var publishRequest = new PublishFormDraftRequest(inReview.RowVersion);
        using HttpResponseMessage publishResponse = await Client.PostAsJsonAsync(
            "/api/forms/intake-assessment/draft/publish",
            publishRequest).ConfigureAwait(false);
        await AssertStatusAsync(publishResponse, HttpStatusCode.OK).ConfigureAwait(false);

        FormVersionDto published = (await publishResponse.Content.ReadFromJsonAsync<FormVersionDto>().ConfigureAwait(false))!;
        Assert.Equal("published", published.Status);
        Assert.Equal("1.0.0", published.Version);
        Assert.False(string.IsNullOrWhiteSpace(published.ContentHash));

        FormVersionDto resolved = await GetVersionAsync("intake-assessment", "1.0.0").ConfigureAwait(false);
        Assert.Equal(published.Id, resolved.Id);
        Assert.Equal(published.ClinicalSchemaJson, resolved.ClinicalSchemaJson);

        using HttpResponseMessage retireResponse = await Client.PostAsync(
            new Uri("/api/forms/intake-assessment/versions/1.0.0/retire", UriKind.Relative),
            content: null).ConfigureAwait(false);
        await AssertStatusAsync(retireResponse, HttpStatusCode.OK).ConfigureAwait(false);

        FormVersionDto retired = (await retireResponse.Content.ReadFromJsonAsync<FormVersionDto>().ConfigureAwait(false))!;
        Assert.Equal("retired", retired.Status);

        FormVersionDto stillResolvable = await GetVersionAsync("intake-assessment", "1.0.0").ConfigureAwait(false);
        Assert.Equal("retired", stillResolvable.Status);

        await AssertAuditEventsRecordedAsync(
            published.Id,
            "form.version.published",
            "form.version.retired").ConfigureAwait(false);
    }

    [Fact]
    public async Task SubmitForReview_LocksDraftUntilWithdrawn()
    {
        var createRequest = new CreateFormRequest(
            "review-flow",
            "Review flow",
            MinimalClinicalSchema("notes", "form.notes"),
            null);

        using HttpResponseMessage createResponse = await Client.PostAsJsonAsync("/api/forms", createRequest).ConfigureAwait(false);
        await AssertStatusAsync(createResponse, HttpStatusCode.Created).ConfigureAwait(false);

        FormVersionDto draft = await GetEditableVersionAsync("review-flow").ConfigureAwait(false);
        var submitRequest = new SubmitFormDraftForReviewRequest(draft.RowVersion);
        using HttpResponseMessage submitResponse = await Client.PostAsJsonAsync(
            "/api/forms/review-flow/draft/submit-review",
            submitRequest).ConfigureAwait(false);
        await AssertStatusAsync(submitResponse, HttpStatusCode.OK).ConfigureAwait(false);

        FormVersionDto inReview = (await submitResponse.Content.ReadFromJsonAsync<FormVersionDto>().ConfigureAwait(false))!;
        Assert.Equal("review", inReview.Status);
        Assert.NotNull(inReview.SubmittedForReviewAt);

        var staleUpdate = new UpdateFormDraftRequest(
            MinimalClinicalSchema("updated-notes", "form.updated-notes"),
            inReview.UiSchemaJson,
            inReview.RulesSchemaJson,
            inReview.RowVersion);
        using HttpResponseMessage updateResponse = await Client.PutAsJsonAsync("/api/forms/review-flow/draft", staleUpdate).ConfigureAwait(false);
        await AssertStatusAsync(updateResponse, HttpStatusCode.NotFound).ConfigureAwait(false);

        var withdrawRequest = new WithdrawFormDraftFromReviewRequest(inReview.RowVersion);
        using HttpResponseMessage withdrawResponse = await Client.PostAsJsonAsync(
            "/api/forms/review-flow/draft/withdraw-review",
            withdrawRequest).ConfigureAwait(false);
        await AssertStatusAsync(withdrawResponse, HttpStatusCode.OK).ConfigureAwait(false);

        FormVersionDto backToDraft = (await withdrawResponse.Content.ReadFromJsonAsync<FormVersionDto>().ConfigureAwait(false))!;
        Assert.Equal("draft", backToDraft.Status);

        FormVersionDto inReviewAgain = await SubmitForReviewAsync("review-flow", backToDraft.RowVersion).ConfigureAwait(false);
        var publishRequest = new PublishFormDraftRequest(inReviewAgain.RowVersion);
        using HttpResponseMessage publishResponse = await Client.PostAsJsonAsync(
            "/api/forms/review-flow/draft/publish",
            publishRequest).ConfigureAwait(false);
        await AssertStatusAsync(publishResponse, HttpStatusCode.OK).ConfigureAwait(false);

        FormVersionDto published = (await publishResponse.Content.ReadFromJsonAsync<FormVersionDto>().ConfigureAwait(false))!;
        Assert.Equal("published", published.Status);
    }

    [Fact]
    public async Task PublishDraft_IsImmutableAfterPublish()
    {
        var createRequest = new CreateFormRequest(
            "follow-up",
            "Follow up",
            MinimalClinicalSchema("follow-up-notes", "follow-up.notes"),
            MinimalUiSchema("follow-up-notes"));

        using HttpResponseMessage createResponse = await Client.PostAsJsonAsync("/api/forms", createRequest).ConfigureAwait(false);
        await AssertStatusAsync(createResponse, HttpStatusCode.Created).ConfigureAwait(false);

        FormVersionDto draft = await GetEditableVersionAsync("follow-up").ConfigureAwait(false);
        FormVersionDto inReview = await SubmitForReviewAsync("follow-up", draft.RowVersion).ConfigureAwait(false);
        var publishRequest = new PublishFormDraftRequest(inReview.RowVersion);
        using HttpResponseMessage publishResponse = await Client.PostAsJsonAsync(
            "/api/forms/follow-up/draft/publish",
            publishRequest).ConfigureAwait(false);
        await AssertStatusAsync(publishResponse, HttpStatusCode.OK).ConfigureAwait(false);

        FormVersionDto published = (await publishResponse.Content.ReadFromJsonAsync<FormVersionDto>().ConfigureAwait(false))!;
        var staleUpdate = new UpdateFormDraftRequest(
            MinimalClinicalSchema("changed-notes", "follow-up.changed-notes"),
            published.UiSchemaJson,
            published.RulesSchemaJson,
            published.RowVersion);

        using HttpResponseMessage updateResponse = await Client.PutAsJsonAsync("/api/forms/follow-up/draft", staleUpdate).ConfigureAwait(false);
        await AssertStatusAsync(updateResponse, HttpStatusCode.NotFound).ConfigureAwait(false);
    }

    [Fact]
    public async Task SoftDeleteDraft_AllowsDeleteAfterPublishedVersionIsRetired()
    {
        var createRequest = new CreateFormRequest(
            "retired-form",
            "Retired form",
            MinimalClinicalSchema("notes", "form.notes"),
            null);

        using HttpResponseMessage createResponse = await Client.PostAsJsonAsync("/api/forms", createRequest).ConfigureAwait(false);
        await AssertStatusAsync(createResponse, HttpStatusCode.Created).ConfigureAwait(false);

        FormVersionDto draft = await GetEditableVersionAsync("retired-form").ConfigureAwait(false);
        FormVersionDto inReview = await SubmitForReviewAsync("retired-form", draft.RowVersion).ConfigureAwait(false);
        var publishRequest = new PublishFormDraftRequest(inReview.RowVersion);
        using HttpResponseMessage publishResponse = await Client.PostAsJsonAsync(
            "/api/forms/retired-form/draft/publish",
            publishRequest).ConfigureAwait(false);
        await AssertStatusAsync(publishResponse, HttpStatusCode.OK).ConfigureAwait(false);

        using HttpResponseMessage retireResponse = await Client.PostAsync(
            new Uri("/api/forms/retired-form/versions/1.0.0/retire", UriKind.Relative),
            content: null).ConfigureAwait(false);
        await AssertStatusAsync(retireResponse, HttpStatusCode.OK).ConfigureAwait(false);

        using HttpResponseMessage createDraftResponse = await Client.PostAsync(
            new Uri("/api/forms/retired-form/draft", UriKind.Relative),
            content: null).ConfigureAwait(false);
        await AssertStatusAsync(createDraftResponse, HttpStatusCode.Created).ConfigureAwait(false);

        using HttpResponseMessage deleteResponse = await Client.DeleteAsync(new Uri("/api/forms/retired-form/draft", UriKind.Relative)).ConfigureAwait(false);
        await AssertStatusAsync(deleteResponse, HttpStatusCode.NoContent).ConfigureAwait(false);

        using HttpResponseMessage getResponse = await Client.GetAsync(new Uri("/api/forms/retired-form", UriKind.Relative)).ConfigureAwait(false);
        await AssertStatusAsync(getResponse, HttpStatusCode.NotFound).ConfigureAwait(false);
    }

    private HttpClient Client { get; }

    private FormWebApplicationFactory Factory { get; } = new();

    private async Task<FormVersionDto> GetEditableVersionAsync(string code)
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri($"/api/forms/{code}/draft", UriKind.Relative)).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.OK).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<FormVersionDto>().ConfigureAwait(false))!;
    }

    private async Task<FormVersionDto> GetVersionAsync(string code, string version)
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri($"/api/forms/{code}/versions/{version}", UriKind.Relative)).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.OK).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<FormVersionDto>().ConfigureAwait(false))!;
    }

    private async Task<FormVersionDto> SubmitForReviewAsync(string code, uint rowVersion)
    {
        using HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/api/forms/{code}/draft/submit-review",
            new SubmitFormDraftForReviewRequest(rowVersion)).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.OK).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<FormVersionDto>().ConfigureAwait(false))!;
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
            Assert.Contains(events, item => item.Action == action);
        }
    }

    private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode == expected)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
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

public sealed class FormWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _sharedConnection = new("Data Source=:memory:");

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        _sharedConnection.Open();

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

            services.AddDbContext<CynaraDbContext>(options => options.UseSqlite(_sharedConnection));
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sharedConnection.Dispose();
        }

        base.Dispose(disposing);
    }
}
