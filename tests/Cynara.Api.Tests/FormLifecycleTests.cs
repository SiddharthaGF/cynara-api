using System.Globalization;
using System.Net;
using System.Text.Json;

using Cynara.Domain.Audit;

namespace Cynara.Api.Tests;

[Collection(PostgresFixtureDefinition.Name)]
public sealed class FormLifecycleTests : IDisposable
{
    public FormLifecycleTests(PostgreSqlDatabaseFixture database)
    {
        Factory = new FormWebApplicationFactory(database.Settings);
        Client = Factory.CreateClient();
        Client.AcceptJsonApi();
        Api = new JsonApiClient(Client);
        Api.UseHospitalContext(Factory.BootstrapOptions.BootstrapCode);
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
        using JsonDocument created = await Api.PostResourceAsync(
            "formDefinitions",
            new
            {
                code = "intake-assessment",
                name = "Intake assessment",
                initialClinicalSchemaJson = MinimalClinicalSchema(
                    "patient-name",
                    "patient.name"),
                initialUiSchemaJson = MinimalUiSchema("patient-name"),
            }).ConfigureAwait(false);

        string definitionId = JsonApiClient.RequireId(created);
        Assert.Equal("intake-assessment", JsonApiClient.AttrString(created, "code"));

        using JsonDocument definition = await Api.GetAsync(
            $"/api/formDefinitions/{definitionId}?include=versions")
            .ConfigureAwait(false);
        JsonElement included = definition.RootElement.GetProperty("included");
        JsonElement draftData = included.EnumerateArray().First(item =>
            string.Equals(
                item.GetProperty("attributes").GetProperty("status").GetString(),
                "draft",
                StringComparison.OrdinalIgnoreCase));
        string draftId = draftData.GetProperty("id").GetString()!;
        Assert.Equal("draft", draftData.GetProperty("attributes").GetProperty("status").GetString());

        uint rowVersion = draftData.GetProperty("attributes").GetProperty("rowVersion").GetUInt32();
        string updatedClinical = MinimalClinicalSchema("patient-full-name", "patient.full-name");
        using JsonDocument updated = await Api.PatchResourceAsync(
            "formVersions",
            draftId,
            new
            {
                clinicalSchemaJson = updatedClinical,
                uiSchemaJson = draftData.GetProperty("attributes").GetProperty("uiSchemaJson").GetString(),
                rulesSchemaJson = (string?)null,
                rowVersion,
            }).ConfigureAwait(false);
        Assert.Equal(1u, JsonApiClient.AttrUInt(updated, "rowVersion"));
        Assert.Contains(
            "patient-full-name",
            JsonApiClient.AttrString(updated, "clinicalSchemaJson"),
            StringComparison.Ordinal);

        using JsonDocument inReview = await PostWorkflowAsync(
            draftId,
            "submit-review",
            JsonApiClient.AttrUInt(updated, "rowVersion"))
            .ConfigureAwait(false);
        using JsonDocument published = await PostWorkflowAsync(
            draftId,
            "publish",
            JsonApiClient.AttrUInt(inReview, "rowVersion"))
            .ConfigureAwait(false);
        Assert.Equal("published", JsonApiClient.AttrString(published, "status"));
        Assert.Equal("1.0.0", JsonApiClient.AttrString(published, "version"));
        Assert.False(string.IsNullOrWhiteSpace(
            JsonApiClient.AttrString(published, "contentHash")));

        using JsonDocument resolved = await Api.GetAsync(
            $"/api/formDefinitions/{definitionId}?include=versions")
            .ConfigureAwait(false);
        Assert.Contains(
            resolved.RootElement.GetProperty("included").EnumerateArray(),
            item => string.Equals(
                item.GetProperty("id").GetString(),
                draftId,
                StringComparison.Ordinal)
                && string.Equals(
                    item.GetProperty("attributes").GetProperty("version").GetString(),
                    "1.0.0",
                    StringComparison.Ordinal));

        using JsonDocument retired = await PostWorkflowAsync(
            draftId,
            "retire",
            rowVersion: null).ConfigureAwait(false);
        Assert.Equal("retired", JsonApiClient.AttrString(retired, "status"));

        await AssertAuditEventsRecordedAsync(
            Guid.Parse(draftId, CultureInfo.InvariantCulture),
            "form.version.published",
            "form.version.retired").ConfigureAwait(false);
    }

    [Fact]
    public async Task SubmitForReview_LocksDraftUntilWithdrawn()
    {
        using JsonDocument created = await Api.PostResourceAsync(
            "formDefinitions",
            new
            {
                code = "review-flow",
                name = "Review flow",
                initialClinicalSchemaJson = MinimalClinicalSchema("notes", "form.notes"),
            }).ConfigureAwait(false);
        string definitionId = JsonApiClient.RequireId(created);
        string draftId = await GetDraftIdAsync(definitionId).ConfigureAwait(false);
        uint rowVersion = await GetRowVersionAsync(draftId).ConfigureAwait(false);

        using JsonDocument inReview = await PostWorkflowAsync(
            draftId,
            "submit-review",
            rowVersion).ConfigureAwait(false);
        Assert.Equal("review", JsonApiClient.AttrString(inReview, "status"));

        using var patchContent = JsonApiClient.CreateJsonApiContent(new
        {
            data = new
            {
                type = "formVersions",
                id = draftId,
                attributes = new
                {
                    clinicalSchemaJson = MinimalClinicalSchema("notes", "form.notes"),
                    rowVersion = JsonApiClient.AttrUInt(inReview, "rowVersion"),
                },
            },
        });
        using var patchRequest = new HttpRequestMessage(
            HttpMethod.Patch,
            new Uri($"/api/formVersions/{draftId}", UriKind.Relative))
        {
            Content = patchContent,
        };
        using HttpResponseMessage patchResponse = await Client
            .SendAsync(patchRequest)
            .ConfigureAwait(false);
        string patchBody = await patchResponse.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.True(
            patchResponse.StatusCode is HttpStatusCode.Conflict
                or HttpStatusCode.UnprocessableEntity
                or HttpStatusCode.BadRequest,
            $"Expected conflict-style status, got {patchResponse.StatusCode}: {patchBody}");

        using JsonDocument withdrawn = await PostWorkflowAsync(
            draftId,
            "withdraw-review",
            JsonApiClient.AttrUInt(inReview, "rowVersion"))
            .ConfigureAwait(false);
        Assert.Equal("draft", JsonApiClient.AttrString(withdrawn, "status"));
    }

    private HttpClient Client { get; }

    private JsonApiClient Api { get; }

    private FormWebApplicationFactory Factory { get; }

    private async Task<JsonDocument> PostWorkflowAsync(
        string versionId,
        string action,
        uint? rowVersion,
        string? comment = null)
    {
        var query = new List<string>();
        if (rowVersion is not null)
        {
            query.Add($"rowVersion={rowVersion.Value}");
        }

        if (!string.IsNullOrWhiteSpace(comment))
        {
            query.Add($"comment={Uri.EscapeDataString(comment)}");
        }

        string suffix = query.Count == 0 ? string.Empty : "?" + string.Join('&', query);
        using HttpResponseMessage response = await Client.PostAsync(
            new Uri(
                $"/api/formVersions/{versionId}/{action}{suffix}",
                UriKind.Relative),
            content: null).ConfigureAwait(false);
        return await JsonApiClient.ReadDocumentAsync(response).ConfigureAwait(false);
    }

    private async Task<string> GetDraftIdAsync(string definitionId)
    {
        using JsonDocument definition = await Api.GetAsync(
            $"/api/formDefinitions/{definitionId}?include=versions")
            .ConfigureAwait(false);
        return definition.RootElement.GetProperty("included")
            .EnumerateArray()
            .First(item => string.Equals(
                item.GetProperty("attributes").GetProperty("status").GetString(),
                "draft",
                StringComparison.OrdinalIgnoreCase))
            .GetProperty("id")
            .GetString()!;
    }

    private async Task<uint> GetRowVersionAsync(string versionId)
    {
        using JsonDocument document = await Api.GetAsync($"/api/formVersions/{versionId}")
            .ConfigureAwait(false);
        return JsonApiClient.AttrUInt(document, "rowVersion");
    }

    private async Task AssertAuditEventsRecordedAsync(
        Guid resourceId,
        params string[] actions)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        List<AuditEvent> events = [.. (await dbContext.AuditEvents
            .Where(item => item.ResourceId == resourceId)
            .ToListAsync()
            .ConfigureAwait(false))
            .OrderBy(item => item.OccurredAt)];

        foreach (string action in actions)
        {
            Assert.Contains(
                events,
                item => string.Equals(item.Action, action, StringComparison.Ordinal));
        }
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
            fields = new Dictionary<string, object>(StringComparer.Ordinal)
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

internal sealed class FormWebApplicationFactory(TestDatabaseSettings database)
    : CynaraWebApplicationFactory(database);
