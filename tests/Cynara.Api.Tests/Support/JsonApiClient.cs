using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Cynara.Api.Tests.Support;

internal static class JsonApiMedia
{
    public const string ContentType = "application/vnd.api+json";
}

internal sealed class JsonApiClient(HttpClient httpClient)
{
    public HttpClient Http { get; } = httpClient;

    public void UseHospitalContext(string? hospitalCode)
    {
        if (string.IsNullOrWhiteSpace(hospitalCode))
        {
            Http.DefaultRequestHeaders.Remove("X-Hospital-Code");
            return;
        }

        if (Http.DefaultRequestHeaders.Contains("X-Hospital-Code"))
        {
            _ = Http.DefaultRequestHeaders.Remove("X-Hospital-Code");
        }

        _ = Http.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            hospitalCode);
    }

    /// <summary>
    /// Replaces the <c>X-Actor-Id</c> seam actor for subsequent requests;
    /// pass null to drop the header.
    /// </summary>
    public void UseActor(string? actorId)
    {
        if (Http.DefaultRequestHeaders.Contains("X-Actor-Id"))
        {
            _ = Http.DefaultRequestHeaders.Remove("X-Actor-Id");
        }

        if (!string.IsNullOrWhiteSpace(actorId))
        {
            Http.DefaultRequestHeaders.Add("X-Actor-Id", actorId);
        }
    }

    public async Task<JsonDocument> PostResourceAsync(
        string resourceType,
        object attributes,
        object? relationships = null,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = resourceType,
            ["attributes"] = attributes,
        };
        if (relationships is not null)
        {
            data["relationships"] = relationships;
        }

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["data"] = data,
        };

        using StringContent content = CreateJsonApiContent(payload);
        using HttpResponseMessage response = await Http
            .PostAsync(
                new Uri($"/api/{resourceType}", UriKind.Relative),
                content,
                cancellationToken)
            .ConfigureAwait(false);
        return await ReadDocumentAsync(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<JsonDocument> PatchResourceAsync(
        string resourceType,
        string id,
        object attributes,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            data = new
            {
                type = resourceType,
                id,
                attributes,
            },
        };
        using StringContent content = CreateJsonApiContent(payload);
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            new Uri($"/api/{resourceType}/{id}", UriKind.Relative))
        {
            Content = content,
        };
        using HttpResponseMessage response = await Http
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        return await ReadDocumentAsync(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<JsonDocument> GetAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await Http
            .GetAsync(new Uri(path, UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);
        return await ReadDocumentAsync(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> SendGetAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        return await Http
            .GetAsync(new Uri(path, UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> DeleteAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        return await Http
            .DeleteAsync(new Uri(path, UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string> CreateWorkflowDefinitionAsync(
        string code,
        string name,
        string workflowSchemaJson)
    {
        using JsonDocument created = await PostResourceAsync(
            "workflowDefinitions",
            new
            {
                code,
                name,
                initialWorkflowSchemaJson = workflowSchemaJson,
            }).ConfigureAwait(false);
        return RequireId(created);
    }

    public async Task<string> FindWorkflowDefinitionIdAsync(string code)
    {
        using JsonDocument list = await GetAsync(
            "/api/workflowDefinitions").ConfigureAwait(false);
        foreach (JsonElement item in list.RootElement
            .GetProperty("data").EnumerateArray())
        {
            if (string.Equals(
                item.GetProperty("attributes").GetProperty("code").GetString(),
                code,
                StringComparison.Ordinal))
            {
                return item.GetProperty("id").GetString()!;
            }
        }

        throw new InvalidOperationException($"Workflow '{code}' not found.");
    }

    public async Task<string> GetDraftVersionIdAsync(string definitionId)
    {
        using JsonDocument definition = await GetAsync(
            $"/api/workflowDefinitions/{definitionId}?include=versions")
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

    public async Task<uint> GetVersionRowVersionAsync(string versionId)
    {
        using JsonDocument document = await GetAsync(
            $"/api/workflowVersions/{versionId}").ConfigureAwait(false);
        return AttrUInt(document, "rowVersion");
    }

    public Task<uint> GetVersionRowVersionAsync(Guid versionId)
    {
        return GetVersionRowVersionAsync(versionId.ToString());
    }

    public async Task<JsonDocument> PostVersionActionAsync(
        string versionId,
        string action,
        uint? rowVersion)
    {
        return await PostActionAsync(
            $"/api/workflowVersions/{versionId}/{action}",
            new { rowVersion }).ConfigureAwait(false);
    }

    public Task<JsonDocument> PostVersionActionAsync(
        Guid versionId,
        string action,
        uint? rowVersion)
    {
        return PostVersionActionAsync(
            versionId.ToString(),
            action,
            rowVersion);
    }

    public async Task<Guid> SeedPatientAsync()
    {
        return await PostPlainForIdAsync(
            "/api/patients",
            new
            {
                mrn = $"MRN-{Guid.NewGuid():N}",
                nationalId = (string?)null,
                givenName = "Ada",
                familyName = "Lovelace",
                birthDate = "1990-01-01",
                sex = "female",
                bloodType = "o+",
            }).ConfigureAwait(false);
    }

    public async Task<(string DefinitionId, string VersionId)>
        CreateAndPublishWorkflowAsync(
            string code,
            string workflowSchemaJson)
    {
        string definitionId = await CreateWorkflowDefinitionAsync(
            code,
            code,
            workflowSchemaJson).ConfigureAwait(false);
        string versionId = await PublishCurrentDraftAsync(definitionId)
            .ConfigureAwait(false);
        return (definitionId, versionId);
    }

    public async Task<string> PublishWorkflowVersionAsync(
        string code,
        string workflowSchemaJson)
    {
        (_, string versionId) = await CreateAndPublishWorkflowAsync(
            code,
            workflowSchemaJson).ConfigureAwait(false);
        return versionId;
    }

    public async Task<string> PublishNextWorkflowVersionAsync(
        string code,
        string? workflowSchemaJson = null)
    {
        string definitionId = await FindWorkflowDefinitionIdAsync(code)
            .ConfigureAwait(false);
        using HttpResponseMessage created = await Http.PostAsync(
            new Uri(
                $"/api/workflowDefinitions/{definitionId}/create-draft",
                UriKind.Relative),
            content: null).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        string draftId = await GetDraftVersionIdAsync(definitionId)
            .ConfigureAwait(false);
        uint rowVersion = workflowSchemaJson is null
            ? await GetVersionRowVersionAsync(draftId).ConfigureAwait(false)
            : await PatchDraftSchemaAsync(
                draftId,
                workflowSchemaJson).ConfigureAwait(false);
        return await SubmitAndPublishAsync(draftId, rowVersion)
            .ConfigureAwait(false);
    }

    private async Task<uint> PatchDraftSchemaAsync(
        string draftId,
        string workflowSchemaJson)
    {
        uint rowVersion = await GetVersionRowVersionAsync(draftId)
            .ConfigureAwait(false);
        using JsonDocument updated = await PatchResourceAsync(
            "workflowVersions",
            draftId,
            new
            {
                workflowSchemaJson,
                rowVersion,
            }).ConfigureAwait(false);
        return AttrUInt(updated, "rowVersion");
    }

    private async Task<string> PublishCurrentDraftAsync(string definitionId)
    {
        string draftId = await GetDraftVersionIdAsync(definitionId)
            .ConfigureAwait(false);
        uint rowVersion = await GetVersionRowVersionAsync(draftId)
            .ConfigureAwait(false);
        return await SubmitAndPublishAsync(draftId, rowVersion)
            .ConfigureAwait(false);
    }

    private async Task<string> SubmitAndPublishAsync(
        string draftId,
        uint rowVersion)
    {
        using JsonDocument inReview = await PostVersionActionAsync(
            draftId,
            "submit-review",
            rowVersion).ConfigureAwait(false);
        using JsonDocument published = await PostVersionActionAsync(
            draftId,
            "publish",
            AttrUInt(inReview, "rowVersion")).ConfigureAwait(false);
        return RequireId(published);
    }

    public async Task<(Guid PatientId, Guid EncounterId)> SeedEncounterAsync()
    {
        Guid patientId = await SeedPatientAsync().ConfigureAwait(false);
        Guid encounterId = await SeedEncounterForPatientAsync(patientId)
            .ConfigureAwait(false);
        return (patientId, encounterId);
    }

    public async Task<Guid> SeedEncounterForPatientAsync(Guid patientId)
    {
        Guid facilityId = await PostPlainForIdAsync(
            "/api/facilities",
            new
            {
                code = $"fac-{Guid.NewGuid():N}",
                name = "Facility",
            }).ConfigureAwait(false);
        Guid clinicalAreaId = await PostPlainForIdAsync(
            "/api/clinicalAreas",
            new
            {
                code = $"area-{Guid.NewGuid():N}",
                name = "Area",
                facilityId,
            }).ConfigureAwait(false);
        return await PostPlainForIdAsync(
            "/api/encounters",
            new
            {
                patientId,
                facilityId,
                clinicalAreaId,
                type = "ambulatory",
                responsibleProfessionalId = "dr-who",
            }).ConfigureAwait(false);
    }

    private async Task<Guid> PostPlainForIdAsync(string path, object body)
    {
        using HttpResponseMessage response = await Http.PostAsync(
            new Uri(path, UriKind.Relative),
            CreateJsonApiContent(body)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return Guid.Parse(
            document.RootElement.GetProperty("id").GetString()!,
            CultureInfo.InvariantCulture);
    }

    public async Task<JsonDocument> PostActionAsync(
        string path,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await PostActionRawAsync(
            path,
            body,
            cancellationToken).ConfigureAwait(false);
        return await ReadDocumentAsync(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> PostActionRawAsync(
        string path,
        object? body,
        CancellationToken cancellationToken = default)
    {
        string resolvedPath = AppendQueryFromObject(path, body);
        return await Http
            .PostAsync(
                new Uri(resolvedPath, UriKind.Relative),
                content: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public static string AppendQueryFromObject(string path, object? body)
    {
        if (body is null)
        {
            return path;
        }

        var pairs = new List<string>();
        foreach (PropertyInfo property in body.GetType().GetProperties(
            BindingFlags.Instance | BindingFlags.Public))
        {
            object? value = property.GetValue(body);
            if (value is null)
            {
                continue;
            }

            string encoded = Uri.EscapeDataString(
                Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
            pairs.Add($"{property.Name}={encoded}");
        }

        if (pairs.Count == 0)
        {
            return path;
        }

        string separator = path.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return path + separator + string.Join('&', pairs);
    }

    public static StringContent CreateJsonApiContent(object payload)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue(
            JsonApiMedia.ContentType);
        return content;
    }

    public static HttpRequestMessage CreatePostRequest(
        string path,
        object body)
    {
        return new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(path, UriKind.Relative))
        {
            Content = CreateJsonApiContent(body),
        };
    }

    public static HttpRequestMessage CreatePatchRequest(
        string path,
        object body)
    {
        return new HttpRequestMessage(
            HttpMethod.Patch,
            new Uri(path, UriKind.Relative))
        {
            Content = CreateJsonApiContent(body),
        };
    }

    public static async Task<JsonDocument> ReadDocumentAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        string text = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"HTTP {(int)response.StatusCode}: {text}"));
        }

        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    public static string RequireId(JsonDocument document)
    {
        return document.RootElement.GetProperty("data").GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Missing data.id");
    }

    public static string? AttrString(JsonDocument document, string name)
    {
        JsonElement attributes = document.RootElement
            .GetProperty("data")
            .GetProperty("attributes");
        return attributes.TryGetProperty(name, out JsonElement value)
            ? value.GetString()
            : null;
    }

    public static uint AttrUInt(JsonDocument document, string name)
    {
        return document.RootElement
            .GetProperty("data")
            .GetProperty("attributes")
            .GetProperty(name)
            .GetUInt32();
    }
}

internal static class JsonApiHttpExtensions
{
    public static void AcceptJsonApi(this HttpClient client)
    {
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(JsonApiMedia.ContentType));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }
}
