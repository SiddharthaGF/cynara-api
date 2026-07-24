using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Cynara.Api.Tests.Support;

/// <summary>Shared JSON:API arrange helpers for lifecycle integration tests.</summary>
internal sealed class JsonApiWorkflow(JsonApiClient api, HttpClient client)
{
    public JsonApiClient Api { get; } = api;

    public HttpClient Client { get; } = client;

    public async Task<string> CreateFormDefinitionAsync(
        string code,
        string name,
        string clinicalSchemaJson,
        string? uiSchemaJson = null,
        string? rulesSchemaJson = null)
    {
        using JsonDocument created = await Api.PostResourceAsync(
            "formDefinitions",
            new
            {
                code,
                name,
                initialClinicalSchemaJson = clinicalSchemaJson,
                initialUiSchemaJson = uiSchemaJson,
                initialRulesSchemaJson = rulesSchemaJson,
            }).ConfigureAwait(false);
        return JsonApiClient.RequireId(created);
    }

    public async Task<string> CreateComponentDefinitionAsync(
        string code,
        string name,
        string clinicalSchemaJson,
        string? uiSchemaJson = null)
    {
        using JsonDocument created = await Api.PostResourceAsync(
            "componentDefinitions",
            new
            {
                code,
                name,
                initialClinicalSchemaJson = clinicalSchemaJson,
                initialUiSchemaJson = uiSchemaJson,
            }).ConfigureAwait(false);
        return JsonApiClient.RequireId(created);
    }

    public async Task<string> GetDraftVersionIdAsync(
        string definitionResource,
        string versionsRel,
        string definitionId)
    {
        using JsonDocument definition = await Api.GetAsync(
            $"/api/{definitionResource}/{definitionId}?include={versionsRel}")
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

    public Task<string> GetFormDraftIdAsync(string definitionId)
    {
        return GetDraftVersionIdAsync(
            "formDefinitions",
            "versions",
            definitionId);
    }

    public Task<string> GetComponentDraftIdAsync(string definitionId)
    {
        return GetDraftVersionIdAsync(
            "componentDefinitions",
            "versions",
            definitionId);
    }

    public async Task<JsonDocument> GetVersionAsync(string resourceType, string id)
    {
        return await Api.GetAsync($"/api/{resourceType}/{id}").ConfigureAwait(false);
    }

    public async Task<uint> GetRowVersionAsync(string resourceType, string id)
    {
        using JsonDocument document = await GetVersionAsync(resourceType, id)
            .ConfigureAwait(false);
        return JsonApiClient.AttrUInt(document, "rowVersion");
    }

    public async Task<JsonDocument> SubmitAndPublishFormAsync(string draftId)
    {
        uint rowVersion = await GetRowVersionAsync("formVersions", draftId)
            .ConfigureAwait(false);
        using JsonDocument inReview = await Api.PostActionAsync(
            $"/api/formVersions/{draftId}/submit-review?rowVersion={rowVersion}")
            .ConfigureAwait(false);
        uint next = JsonApiClient.AttrUInt(inReview, "rowVersion");
        return await Api.PostActionAsync(
            $"/api/formVersions/{draftId}/publish?rowVersion={next}")
            .ConfigureAwait(false);
    }

    public async Task PublishComponentAsync(string draftId)
    {
        uint rowVersion = await GetRowVersionAsync("componentVersions", draftId)
            .ConfigureAwait(false);
        using JsonDocument unusedDoc = await Api.PostActionAsync(
            $"/api/componentVersions/{draftId}/publish?rowVersion={rowVersion}")
            .ConfigureAwait(false);
    }

    public async Task<(string DefinitionId, string VersionId)> PublishFormAsync(
        string code,
        string name,
        string clinicalSchemaJson,
        string? uiSchemaJson = null,
        string? rulesSchemaJson = null)
    {
        string definitionId = await CreateFormDefinitionAsync(
            code,
            name,
            clinicalSchemaJson,
            uiSchemaJson,
            rulesSchemaJson).ConfigureAwait(false);
        string draftId = await GetFormDraftIdAsync(definitionId).ConfigureAwait(false);
        using JsonDocument published = await SubmitAndPublishFormAsync(draftId)
            .ConfigureAwait(false);
        return (definitionId, JsonApiClient.RequireId(published));
    }

    public async Task<JsonDocument> CreateResponseAsync(
        string formVersionId,
        string? answersJson = null)
    {
        return await Api.PostResourceAsync(
            "formResponses",
            new { answersJson = answersJson ?? "{}" },
            new
            {
                formVersion = new
                {
                    data = new { type = "formVersions", id = formVersionId },
                },
            }).ConfigureAwait(false);
    }

    public static string MinimalClinicalSchema(string id, string code)
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

    public static string MinimalUiSchema(string fieldId, string label = "Field label")
    {
        return JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0.0",
            clinicalSchemaVersion = "1.0.0",
            fields = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [fieldId] = new
                {
                    label,
                    widget = "text-input",
                },
            },
        });
    }

    public static async Task AssertStatusAsync(
        HttpResponseMessage response,
        HttpStatusCode expected)
    {
        if (response.StatusCode == expected)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.Fail(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Expected {(int)expected} {expected}, got {(int)response.StatusCode} {response.StatusCode}. Body: {body}"));
    }
}
