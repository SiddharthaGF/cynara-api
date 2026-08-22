using System.Globalization;
using System.Net;

using Cynara.Api.Tests.Support;

namespace Cynara.Api.Tests;

/// <summary>
/// Regression guard for read-query budgets. Every request surfaces its SQL
/// read-command count via the <c>X-Query-Count</c> response header (see
/// <c>QueryCountingMiddleware</c>), so a change that turns a bounded read
/// path into an N+1 query explosion fails the build instead of surfacing as
/// a slow endpoint in production.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
[Trait("Category", "E2E")]
public sealed class QueryBudgetTests : IDisposable
{
    private const int SeedFormCount = 5;

    private readonly FormWebApplicationFactory factory;
    private readonly HttpClient client;
    private readonly JsonApiClient api;
    private readonly JsonApiWorkflow workflow;

    public QueryBudgetTests(PostgreSqlDatabaseFixture database)
    {
        factory = new FormWebApplicationFactory(database.Settings);
        client = factory.CreateClient();
        client.AcceptJsonApi();
        api = new JsonApiClient(client);
        api.UseHospitalContext(factory.BootstrapOptions.BootstrapCode);
        workflow = new JsonApiWorkflow(api, client);
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task FormDefinitions_list_with_versions_stays_at_constant_query_budget()
    {
        for (int i = 0; i < SeedFormCount; i++)
        {
            _ = await workflow.PublishFormAsync(
                string.Create(CultureInfo.InvariantCulture, $"budget-form-{i}"),
                string.Create(CultureInfo.InvariantCulture, $"Budget form {i}"),
                JsonApiWorkflow.MinimalClinicalSchema(
                    string.Create(CultureInfo.InvariantCulture, $"f{i}"),
                    string.Create(CultureInfo.InvariantCulture, $"form.f{i}")))
                .ConfigureAwait(false);
        }

        using HttpResponseMessage response = await api.SendGetAsync(
            "/api/formDefinitions?include=versions")
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        int queryCount = ReadQueryCount(response);

        // Loading N forms each with a published version must stay O(1): one
        // list query that eagerly loads versions, not one query per form. A
        // regression toward lazy loading or per-row fetches pushes the count
        // toward SeedFormCount + 1 and fails the assert.
        Assert.InRange(queryCount, 1, 6);
    }

    private static int ReadQueryCount(HttpResponseMessage response)
    {
        Assert.True(
            response.Headers.TryGetValues(
                "X-Query-Count",
                out IEnumerable<string>? values),
            "Response is missing the X-Query-Count observability header.");
        return int.Parse(values.Single(), CultureInfo.InvariantCulture);
    }
}
