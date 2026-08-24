using System.Net;
using System.Text;
using System.Text.Json;

namespace Cynara.Api.Tests.Workflows;

/// <summary>
/// Cross-tenant isolation for workflow pipelines: one hospital cannot read,
/// list, or advance another hospital's pipelines.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class PipelineTenantIsolationTests : IDisposable
{
    public PipelineTenantIsolationTests(PostgreSqlDatabaseFixture database)
    {
        Factory = new CynaraTenantWebApplicationFactory(database.Settings);
        Client = Factory.CreateClient();
        Client.AcceptJsonApi();
        OtherClient = Factory.CreateClient();
        OtherClient.AcceptJsonApi();
        Api = new JsonApiClient(Client);
        Api.UseHospitalContext(CynaraTenantWebApplicationFactory.PrimaryCode);
        OtherApi = new JsonApiClient(OtherClient);
        OtherApi.UseHospitalContext(CynaraTenantWebApplicationFactory.OtherCode);

        Factory.EnsureBootstrapHospitalAsync().GetAwaiter().GetResult();
        Factory.SeedSecondaryHospitalAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        Client.Dispose();
        OtherClient.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task CrossTenant_Pipeline_IsNotVisible()
    {
        _ = await Api.PublishWorkflowVersionAsync(
            "isolation-pipeline",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);

        using JsonDocument started = await StartPipelineAsync(
            "isolation-pipeline").ConfigureAwait(false);
        string pipelineId = started.RootElement.GetProperty("id").GetString()!;

        using HttpResponseMessage get = await OtherClient.GetAsync(
            new Uri($"/api/pipelines/{pipelineId}", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        using HttpResponseMessage list = await OtherClient.GetAsync(
            new Uri("/api/pipelines", UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listBody = JsonDocument.Parse(
            await list.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Empty(listBody.RootElement.GetProperty("pipelines").EnumerateArray());

        using HttpResponseMessage advance = await OtherClient.PostAsync(
            new Uri($"/api/pipelines/{pipelineId}/advance", UriKind.Relative),
            new StringContent(
                /*lang=json,strict*/ """{ "rowVersion": 0 }""",
                Encoding.UTF8,
                "application/json")).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, advance.StatusCode);
    }

    private CynaraTenantWebApplicationFactory Factory { get; }

    private HttpClient Client { get; }

    private HttpClient OtherClient { get; }

    private JsonApiClient Api { get; }

    private JsonApiClient OtherApi { get; }

    private async Task<JsonDocument> StartPipelineAsync(string workflowCode)
    {
        (Guid _, Guid encounterId) = await Api.SeedEncounterAsync().ConfigureAwait(false);
        var payload = new
        {
            workflowCode,
            subjectType = "encounter",
            subjectId = encounterId,
        };
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/api/pipelines", UriKind.Relative))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"),
        };
        using HttpResponseMessage response = await Client.SendAsync(request)
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }
}
