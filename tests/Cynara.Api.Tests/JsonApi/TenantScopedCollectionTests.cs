using System.Globalization;
using System.Net;
using System.Text.Json;

using Cynara.Api.Tests.Workflows;

namespace Cynara.Api.Tests.JsonApi;

/// <summary>
/// Cross-tenant isolation and page-slice correctness for JSON:API collection
/// reads. The hospital predicate must be applied in SQL before pagination
/// and counting, so foreign rows can never consume page slots or inflate
/// <c>meta.total</c>, while same-hospital rows stay fully visible.
///
/// Covers <c>FormDefinitionResourceService</c> end to end over HTTP plus
/// representative subclasses of the shared tenant-scoped repository:
/// <c>WorkflowDefinitionResourceService</c> and
/// <c>AuditEventResourceService</c>. The remaining resource services reuse
/// the exact same repository override and registration path.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class TenantScopedCollectionTests : IDisposable
{
    private const string PrimaryCode = "primary";
    private const string OtherCode = "secondary";

    public TenantScopedCollectionTests(PostgreSqlDatabaseFixture database)
    {
        Factory = new CynaraTenantWebApplicationFactory(database.Settings);
        Client = Factory.CreateClient();
        Client.AcceptJsonApi();
        OtherClient = Factory.CreateClient();
        OtherClient.AcceptJsonApi();

        Client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            PrimaryCode);
        OtherClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            OtherCode);
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

    /// <summary>
    /// With foreign definitions present, walking pages must yield exactly
    /// the caller's own rows once each, prove that slicing happens after
    /// the tenant filter (never over a global slice), and report a scoped
    /// <c>meta.total</c>.
    /// </summary>
    [Fact]
    public async Task FormDefinitions_PageSlices_ContainOnlyTenantRows()
    {
        await ResetAndSeedHospitalsAsync().ConfigureAwait(false);
        var primaryApi = new JsonApiClient(Client);
        var otherApi = new JsonApiClient(OtherClient);

        for (int index = 1; index <= 3; index++)
        {
            string suffix = index.ToString(CultureInfo.InvariantCulture);
            _ = await otherApi.PostResourceAsync(
                "formDefinitions",
                new
                {
                    code = $"tsv-foreign-form-{suffix}",
                    name = $"Foreign form {suffix}",
                    initialClinicalSchemaJson =
                        JsonApiWorkflow.MinimalClinicalSchema(
                            "field", "field.code"),
                }).ConfigureAwait(false);
        }

        List<string> ownIds = [];
        for (int index = 1; index <= 4; index++)
        {
            string suffix = index.ToString(CultureInfo.InvariantCulture);
            using JsonDocument created = await primaryApi.PostResourceAsync(
                "formDefinitions",
                new
                {
                    code = $"tsv-own-form-{suffix}",
                    name = $"Own form {suffix}",
                    initialClinicalSchemaJson =
                        JsonApiWorkflow.MinimalClinicalSchema(
                            "field", "field.code"),
                }).ConfigureAwait(false);
            ownIds.Add(JsonApiClient.RequireId(created));
        }

        using JsonDocument unpaged = await primaryApi
            .GetAsync("/api/formDefinitions")
            .ConfigureAwait(false);
        List<string> listedIds = DataIds(unpaged);
        Assert.Equal(4, TotalCount(unpaged));
        Assert.True(
            listedIds.Count == ownIds.Count
                && ownIds.TrueForAll(listedIds.Contains),
            "Unpaged listing must contain every own definition.");

        HashSet<string> seenIds = [];
        for (int pageNumber = 1; pageNumber <= 3; pageNumber++)
        {
            string pageUri = "/api/formDefinitions?page%5Bsize%5D=2"
                + "&page%5Bnumber%5D="
                + pageNumber.ToString(CultureInfo.InvariantCulture);
            using JsonDocument page = await primaryApi
                .GetAsync(pageUri)
                .ConfigureAwait(false);
            List<string> pageIds = DataIds(page);
            Assert.Equal(4, TotalCount(page));
            Assert.True(
                pageIds.Count <= 2,
                "A page must never exceed its requested size.");
            foreach (string id in pageIds)
            {
                Assert.Contains(id, ownIds);
                Assert.True(
                    seenIds.Add(id),
                    "Pages must not repeat rows across slices.");
            }
        }

        Assert.Equal(ownIds.Count, seenIds.Count);
    }

    [Fact]
    public async Task WorkflowDefinitions_ForeignVolume_IsExcluded()
    {
        await ResetAndSeedHospitalsAsync().ConfigureAwait(false);
        var primaryApi = new JsonApiClient(Client);
        var otherApi = new JsonApiClient(OtherClient);

        for (int index = 1; index <= 3; index++)
        {
            string suffix = index.ToString(CultureInfo.InvariantCulture);
            _ = await otherApi.PostResourceAsync(
                "workflowDefinitions",
                new
                {
                    code = $"tsv-foreign-workflow-{suffix}",
                    name = $"Foreign workflow {suffix}",
                    initialWorkflowSchemaJson =
                        WorkflowTestSchemas.Minimal(),
                }).ConfigureAwait(false);
        }

        using JsonDocument created = await primaryApi.PostResourceAsync(
            "workflowDefinitions",
            new
            {
                code = "tsv-own-workflow",
                name = "Own workflow",
                initialWorkflowSchemaJson = WorkflowTestSchemas.Minimal(),
            }).ConfigureAwait(false);
        string ownId = JsonApiClient.RequireId(created);

        using JsonDocument listing = await primaryApi
            .GetAsync("/api/workflowDefinitions")
            .ConfigureAwait(false);
        List<string> listedIds = DataIds(listing);
        Assert.Equal(1, TotalCount(listing));
        Assert.Equal([ownId], listedIds);
    }

    /// <summary>
    /// The audit view must stay byte-stable while another hospital generates
    /// audit volume, and keep growing only for the caller's own mutations.
    /// </summary>
    [Fact]
    public async Task AuditEvents_ForeignVolume_NeverEntersView()
    {
        await ResetAndSeedHospitalsAsync().ConfigureAwait(false);
        var primaryApi = new JsonApiClient(Client);
        var otherApi = new JsonApiClient(OtherClient);

        using JsonDocument baseline = await primaryApi
            .GetAsync("/api/auditEvents")
            .ConfigureAwait(false);
        int beforeForeignVolume = TotalCount(baseline);

        for (int index = 1; index <= 3; index++)
        {
            string suffix = index.ToString(CultureInfo.InvariantCulture);
            _ = await otherApi.PostResourceAsync(
                "workflowDefinitions",
                new
                {
                    code = $"tsv-audit-foreign-{suffix}",
                    name = $"Audit foreign {suffix}",
                    initialWorkflowSchemaJson =
                        WorkflowTestSchemas.Minimal(),
                }).ConfigureAwait(false);
        }

        using JsonDocument afterForeignVolume = await primaryApi
            .GetAsync("/api/auditEvents")
            .ConfigureAwait(false);
        Assert.Equal(
            beforeForeignVolume,
            TotalCount(afterForeignVolume));

        _ = await primaryApi.PostResourceAsync(
            "workflowDefinitions",
            new
            {
                code = "tsv-audit-own",
                name = "Audit own",
                initialWorkflowSchemaJson = WorkflowTestSchemas.Minimal(),
            }).ConfigureAwait(false);

        using JsonDocument afterOwnMutation = await primaryApi
            .GetAsync("/api/auditEvents")
            .ConfigureAwait(false);
        Assert.True(
            TotalCount(afterOwnMutation) > beforeForeignVolume,
            "Own mutations must still extend the visible audit trail.");
    }

    [Fact]
    public async Task FormDefinitions_CrossTenant_Get_IsNotFound()
    {
        await ResetAndSeedHospitalsAsync().ConfigureAwait(false);
        var primaryApi = new JsonApiClient(Client);

        using JsonDocument created = await primaryApi.PostResourceAsync(
            "formDefinitions",
            new
            {
                code = "tsv-isolation-form",
                name = "Isolation form",
                initialClinicalSchemaJson =
                    JsonApiWorkflow.MinimalClinicalSchema(
                        "field", "field.code"),
            }).ConfigureAwait(false);
        string ownId = JsonApiClient.RequireId(created);

        using HttpResponseMessage response = await OtherClient
            .GetAsync(
                new Uri($"/api/formDefinitions/{ownId}", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task ResetAndSeedHospitalsAsync()
    {
        await Factory.ResetDatabaseAsync().ConfigureAwait(false);
        await Factory.EnsureBootstrapHospitalAsync().ConfigureAwait(false);
        await Factory.SeedSecondaryHospitalAsync().ConfigureAwait(false);
    }

    private static List<string> DataIds(JsonDocument document)
    {
        List<string> ids = [];
        foreach (JsonElement item in document.RootElement
            .GetProperty("data").EnumerateArray())
        {
            ids.Add(item.GetProperty("id").GetString()
                ?? throw new InvalidOperationException(
                    "JSON:API resource without an id."));
        }

        return ids;
    }

    private static int TotalCount(JsonDocument document)
    {
        return document.RootElement
            .GetProperty("meta")
            .GetProperty("total")
            .GetInt32();
    }
}
