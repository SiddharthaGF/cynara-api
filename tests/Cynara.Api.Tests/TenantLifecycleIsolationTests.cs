using System.Net;
using System.Text.Json;

namespace Cynara.Api.Tests;

/// <summary>
/// CYN-34 cross-tenant lifecycle isolation tests. Verifies that one hospital
/// cannot trigger publish, retire, submit-review, or response-creation
/// lifecycle actions against another hospital's resources.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class TenantLifecycleIsolationTests : IDisposable
{
    private const string PrimaryHospitalCode = "primary";
    private const string OtherHospitalCode = "secondary";

    public TenantLifecycleIsolationTests(PostgreSqlDatabaseFixture database)
    {
        Factory = new CynaraTenantWebApplicationFactory(database.Settings);
        Client = Factory.CreateAuthenticatedClientAsync(
            hospitalCode: PrimaryHospitalCode).GetAwaiter().GetResult();
        OtherClient = Factory.CreateAuthenticatedClientAsync(
            hospitalCode: OtherHospitalCode).GetAwaiter().GetResult();

        Factory.SeedSecondaryHospitalAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        Client.Dispose();
        OtherClient.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    private CynaraTenantWebApplicationFactory Factory { get; }

    private HttpClient Client { get; }

    private HttpClient OtherClient { get; }

    [Fact]
    public async Task CrossTenant_ComponentPublish_IsRejected()
    {
        var primaryApi = new JsonApiClient(Client);
        using JsonDocument created = await primaryApi.PostResourceAsync(
            "componentDefinitions",
            new
            {
                code = "isolation-component",
                name = "Isolation component",
                initialClinicalSchemaJson =
                    /*lang=json,strict*/ """{"schemaVersion":"1.0.0","fields":[{"id":"field","code":"comp.field","type":"text"}]}""",
            }).ConfigureAwait(false);
        string definitionId = JsonApiClient.RequireId(created);

        using JsonDocument definition = await primaryApi
            .GetAsync(
                $"/api/componentDefinitions/{definitionId}?include=versions")
            .ConfigureAwait(false);
        string draftId = definition.RootElement.GetProperty("included")
            .EnumerateArray()
            .First(item => string.Equals(
                item.GetProperty("attributes")
                    .GetProperty("status")
                    .GetString(),
                "draft",
                StringComparison.OrdinalIgnoreCase))
            .GetProperty("id")
            .GetString()!;

        using HttpClient secondaryClient = await Factory
            .CreateAuthenticatedClientAsync(hospitalCode: OtherHospitalCode)
            .ConfigureAwait(false);
        uint rowVersion = await GetRowVersionAsync(
            Client, "componentVersions", draftId).ConfigureAwait(false);
        using HttpResponseMessage publishResponse = await secondaryClient
            .PostAsync(
                new Uri(
                    $"/api/componentVersions/{draftId}/publish?rowVersion={rowVersion}",
                    UriKind.Relative),
                content: null)
            .ConfigureAwait(false);
        Assert.True(
            publishResponse.StatusCode == HttpStatusCode.NotFound,
            "Expected 404 for cross-tenant publish, "
            + $"got {publishResponse.StatusCode}.");
    }

    [Fact]
    public async Task CrossTenant_ComponentRetire_IsRejected()
    {
        var primaryApi = new JsonApiClient(Client);
        using JsonDocument created = await primaryApi.PostResourceAsync(
            "componentDefinitions",
            new
            {
                code = "retire-isolation-component",
                name = "Retire isolation component",
                initialClinicalSchemaJson =
                    /*lang=json,strict*/ """{"schemaVersion":"1.0.0","fields":[{"id":"field","code":"comp.field","type":"text"}]}""",
            }).ConfigureAwait(false);
        string definitionId = JsonApiClient.RequireId(created);

        using JsonDocument definition = await primaryApi
            .GetAsync(
                $"/api/componentDefinitions/{definitionId}?include=versions")
            .ConfigureAwait(false);
        string draftId = definition.RootElement.GetProperty("included")
            .EnumerateArray()
            .First(item => string.Equals(
                item.GetProperty("attributes")
                    .GetProperty("status")
                    .GetString(),
                "draft",
                StringComparison.OrdinalIgnoreCase))
            .GetProperty("id")
            .GetString()!;

        uint publishRowVersion = await GetRowVersionAsync(
            Client, "componentVersions", draftId).ConfigureAwait(false);
        using JsonDocument published = await primaryApi.PostActionAsync(
            $"/api/componentVersions/{draftId}/publish?rowVersion={publishRowVersion}")
            .ConfigureAwait(false);
        string publishedId = JsonApiClient.RequireId(published);

        using HttpClient secondaryClient = await Factory
            .CreateAuthenticatedClientAsync(hospitalCode: OtherHospitalCode)
            .ConfigureAwait(false);
        using HttpResponseMessage retireResponse = await secondaryClient
            .PostAsync(
                new Uri(
                    $"/api/componentVersions/{publishedId}/retire",
                    UriKind.Relative),
                content: null)
            .ConfigureAwait(false);
        Assert.True(
            retireResponse.StatusCode == HttpStatusCode.NotFound,
            "Expected 404 for cross-tenant retire, "
            + $"got {retireResponse.StatusCode}.");
    }

    [Fact]
    public async Task CrossTenant_FormVersion_Publish_IsRejected()
    {
        var primaryApi = new JsonApiClient(Client);
        using JsonDocument created = await primaryApi.PostResourceAsync(
            "formDefinitions",
            new
            {
                code = "cross-tenant-publish",
                name = "Cross-tenant publish form",
                initialClinicalSchemaJson =
                    JsonApiWorkflow.MinimalClinicalSchema(
                        "field", "field.code"),
            }).ConfigureAwait(false);
        string definitionId = JsonApiClient.RequireId(created);

        using JsonDocument definition = await primaryApi
            .GetAsync(
                $"/api/formDefinitions/{definitionId}?include=versions")
            .ConfigureAwait(false);
        string draftId = definition.RootElement.GetProperty("included")
            .EnumerateArray()
            .First(item => string.Equals(
                item.GetProperty("attributes")
                    .GetProperty("status")
                    .GetString(),
                "draft",
                StringComparison.OrdinalIgnoreCase))
            .GetProperty("id")
            .GetString()!;

        using HttpClient secondaryClient = await Factory
            .CreateAuthenticatedClientAsync(hospitalCode: OtherHospitalCode)
            .ConfigureAwait(false);
        uint rowVersion = await GetRowVersionAsync(
            Client, "formVersions", draftId).ConfigureAwait(false);
        using HttpResponseMessage submitResponse = await secondaryClient
            .PostAsync(
                new Uri(
                    $"/api/formVersions/{draftId}/submit-review?rowVersion={rowVersion}",
                    UriKind.Relative),
                content: null)
            .ConfigureAwait(false);
        Assert.True(
            submitResponse.StatusCode == HttpStatusCode.NotFound,
            "Expected 404 for cross-tenant submit-review, "
            + $"got {submitResponse.StatusCode}.");
    }

    [Fact]
    public async Task CrossTenant_FormResponse_Creation_IsRejected()
    {
        var primaryApi = new JsonApiClient(Client);
        using JsonDocument created = await primaryApi.PostResourceAsync(
            "formDefinitions",
            new
            {
                code = "cross-tenant-response",
                name = "Cross-tenant response form",
                initialClinicalSchemaJson =
                    JsonApiWorkflow.MinimalClinicalSchema(
                        "patient-name", "patient.name"),
            }).ConfigureAwait(false);
        string definitionId = JsonApiClient.RequireId(created);

        using JsonDocument definition = await primaryApi
            .GetAsync(
                $"/api/formDefinitions/{definitionId}?include=versions")
            .ConfigureAwait(false);
        string draftId = definition.RootElement.GetProperty("included")
            .EnumerateArray()
            .First(item => string.Equals(
                item.GetProperty("attributes")
                    .GetProperty("status")
                    .GetString(),
                "draft",
                StringComparison.OrdinalIgnoreCase))
            .GetProperty("id")
            .GetString()!;
        uint submitVersion = await GetRowVersionAsync(
            Client, "formVersions", draftId).ConfigureAwait(false);
        using JsonDocument inReview = await primaryApi.PostActionAsync(
            $"/api/formVersions/{draftId}/submit-review?rowVersion={submitVersion}")
            .ConfigureAwait(false);
        uint publishVersion = JsonApiClient.AttrUInt(
            inReview, "rowVersion");
        using JsonDocument published = await primaryApi.PostActionAsync(
            $"/api/formVersions/{draftId}/publish?rowVersion={publishVersion}")
            .ConfigureAwait(false);
        string publishedVersionId = JsonApiClient.RequireId(published);

        using HttpClient secondaryClient = await Factory
            .CreateAuthenticatedClientAsync(hospitalCode: OtherHospitalCode)
            .ConfigureAwait(false);
        using HttpResponseMessage response = await secondaryClient
            .SendAsync(new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(
                    "/api/formResponses", UriKind.Relative),
                Content = JsonApiClient.CreateJsonApiContent(new
                {
                    type = "formResponses",
                    attributes = new { answersJson = "{}" },
                    relationships = new
                    {
                        formVersion = new
                        {
                            data = new
                            {
                                type = "formVersions",
                                id = publishedVersionId,
                            },
                        },
                    },
                }),
            })
            .ConfigureAwait(false);
        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound
                or HttpStatusCode.UnprocessableEntity,
            "Expected 404 or 422 for cross-tenant response creation, "
            + $"got {response.StatusCode}.");
    }

    private static async Task<uint> GetRowVersionAsync(
        HttpClient client,
        string resourceType,
        string id)
    {
        using HttpResponseMessage response = await client
            .GetAsync(
                new Uri($"/api/{resourceType}/{id}", UriKind.Relative))
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using JsonDocument document = await JsonApiClient
            .ReadDocumentAsync(response)
            .ConfigureAwait(false);
        return JsonApiClient.AttrUInt(document, "rowVersion");
    }
}
