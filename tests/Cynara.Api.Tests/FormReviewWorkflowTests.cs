using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Cynara.Application.Forms;
using Cynara.Domain.Audit;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Api.Tests;

public sealed class FormReviewWorkflowTests : IDisposable
{
    public FormReviewWorkflowTests()
    {
        Client = Factory.CreateClient();
        Client.DefaultRequestHeaders.Add("X-Actor-Id", "reviewer-1");
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task SubmitForReview_WithInvalidDependencies_Fails()
    {
        string clinical = FormWithComponentRef("section", "section.patient", "missing-component", "9.9.9");
        await CreateFormAsync("broken-review", "Broken review", clinical).ConfigureAwait(false);

        FormVersionDto draft = await GetEditableVersionAsync("broken-review").ConfigureAwait(false);
        using HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/api/forms/broken-review/draft/submit-review",
            new SubmitFormDraftForReviewRequest(draft.RowVersion)).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.Contains("COMPONENT_VERSION_NOT_FOUND", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publish_FromDraftWithoutReview_Fails()
    {
        await CreateFormAsync("direct-publish", "Direct publish", MinimalClinicalSchema("notes", "form.notes")).ConfigureAwait(false);

        FormVersionDto draft = await GetEditableVersionAsync("direct-publish").ConfigureAwait(false);
        using HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/api/forms/direct-publish/draft/publish",
            new PublishFormDraftRequest(draft.RowVersion)).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RejectReview_ReturnsToDraftWithComment()
    {
        await CreateFormAsync("reject-flow", "Reject flow", MinimalClinicalSchema("notes", "form.notes")).ConfigureAwait(false);

        FormVersionDto draft = await GetEditableVersionAsync("reject-flow").ConfigureAwait(false);
        FormVersionDto inReview = await SubmitForReviewAsync("reject-flow", draft.RowVersion).ConfigureAwait(false);

        var rejectRequest = new RejectFormReviewRequest("Needs clearer field labels.", inReview.RowVersion);
        using HttpResponseMessage rejectResponse = await Client.PostAsJsonAsync(
            "/api/forms/reject-flow/draft/reject-review",
            rejectRequest).ConfigureAwait(false);
        await AssertStatusAsync(rejectResponse, HttpStatusCode.OK).ConfigureAwait(false);

        FormVersionDto rejected = (await rejectResponse.Content.ReadFromJsonAsync<FormVersionDto>().ConfigureAwait(false))!;
        Assert.Equal("draft", rejected.Status);
        Assert.Equal("rejected", rejected.LastReviewDecision);
        Assert.Equal("Needs clearer field labels.", rejected.LastReviewComment);
        Assert.NotNull(rejected.LastReviewedAt);
    }

    [Fact]
    public async Task Publish_RecordsActorTimestampSchemaVersionAndHash()
    {
        await CreateFormAsync("publish-metadata", "Publish metadata", MinimalClinicalSchema("notes", "form.notes")).ConfigureAwait(false);

        FormVersionDto draft = await GetEditableVersionAsync("publish-metadata").ConfigureAwait(false);
        FormVersionDto published = await SubmitAndPublishAsync("publish-metadata", draft.RowVersion).ConfigureAwait(false);

        Assert.Equal("published", published.Status);
        Assert.Equal("1.0.0", published.Version);
        Assert.Equal("1.0.0", published.PublishedSchemaVersion);
        Assert.False(string.IsNullOrWhiteSpace(published.ContentHash));
        Assert.Equal("approved", published.LastReviewDecision);
        Assert.NotNull(published.PublishedAt);
        Assert.NotNull(published.LastReviewedAt);

        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider.GetRequiredService<CynaraDbContext>();
        AuditEvent? publishedEvent = await dbContext.AuditEvents.SingleOrDefaultAsync(
            item => item.ResourceId == published.Id && item.Action == "form.version.published").ConfigureAwait(false);
        Assert.NotNull(publishedEvent);
        Assert.Equal("reviewer-1", publishedEvent.ActorId);
        Assert.Contains("schemaVersion", publishedEvent.MetadataJson, StringComparison.Ordinal);
        Assert.Contains("contentHash", publishedEvent.MetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetiredVersion_CannotCreateNewResponse_ButRemainsResolvable()
    {
        await CreateFormAsync("retired-responses", "Retired responses", MinimalClinicalSchema("notes", "form.notes")).ConfigureAwait(false);

        FormVersionDto draft = await GetEditableVersionAsync("retired-responses").ConfigureAwait(false);
        FormVersionDto published = await SubmitAndPublishAsync("retired-responses", draft.RowVersion).ConfigureAwait(false);

        using HttpResponseMessage retireResponse = await Client.PostAsync(
            new Uri($"/api/forms/retired-responses/versions/{published.Version}/retire", UriKind.Relative),
            content: null).ConfigureAwait(false);
        await AssertStatusAsync(retireResponse, HttpStatusCode.OK).ConfigureAwait(false);

        using HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            $"/api/forms/retired-responses/versions/{published.Version}/responses",
            new CreateFormResponseRequest()).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, createResponse.StatusCode);

        FormVersionDto resolved = await GetVersionAsync("retired-responses", published.Version!).ConfigureAwait(false);
        Assert.Equal("retired", resolved.Status);
    }

    private HttpClient Client { get; }

    private FormWebApplicationFactory Factory { get; } = new();

    private async Task CreateFormAsync(string code, string name, string clinicalSchemaJson)
    {
        var request = new CreateFormRequest(code, name, clinicalSchemaJson, UiSchemaJson: null);
        using HttpResponseMessage response = await Client.PostAsJsonAsync("/api/forms", request).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.Created).ConfigureAwait(false);
    }

    private async Task<FormVersionDto> GetEditableVersionAsync(string code)
    {
        using HttpResponseMessage response = await Client.GetAsync(
            new Uri($"/api/forms/{code}/draft", UriKind.Relative)).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.OK).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<FormVersionDto>().ConfigureAwait(false))!;
    }

    private async Task<FormVersionDto> GetVersionAsync(string code, string version)
    {
        using HttpResponseMessage response = await Client.GetAsync(
            new Uri($"/api/forms/{code}/versions/{version}", UriKind.Relative)).ConfigureAwait(false);
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

    private async Task<FormVersionDto> SubmitAndPublishAsync(string code, uint draftRowVersion)
    {
        FormVersionDto inReview = await SubmitForReviewAsync(code, draftRowVersion).ConfigureAwait(false);
        using HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/api/forms/{code}/draft/publish",
            new PublishFormDraftRequest(inReview.RowVersion)).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.OK).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<FormVersionDto>().ConfigureAwait(false))!;
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

    private static string FormWithComponentRef(
        string id,
        string code,
        string componentCode,
        string componentVersion)
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
                    type = "component-ref",
                    componentCode,
                    componentVersion,
                },
            },
        });
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
}
