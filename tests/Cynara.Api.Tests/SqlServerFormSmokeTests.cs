using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Cynara.Api.Tests.Support;
using Cynara.Application.Forms;

namespace Cynara.Api.Tests;

[Collection(MsSqlFixtureDefinition.Name)]
[Trait("Category", "MsSql")]
public sealed class SqlServerFormSmokeTests : IDisposable
{
    private readonly FormWebApplicationFactory factory;
    private readonly HttpClient client;

    public SqlServerFormSmokeTests(MsSqlDatabaseFixture database)
    {
        ArgumentNullException.ThrowIfNull(database);

        factory = new FormWebApplicationFactory(database.Settings);
        client = factory.CreateClient();
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task FormLifecycle_CreateDraft_OnSqlServer()
    {
        string code = $"mssql-intake-{Guid.NewGuid():N}";
        var createRequest = new CreateFormRequest(
            code,
            "MsSql intake",
            MinimalClinicalSchema("patient-name", "patient.name"),
            MinimalUiSchema("patient-name"));

        using HttpResponseMessage createResponse = await client
            .PostAsJsonAsync("/api/forms", createRequest)
            .ConfigureAwait(false);

        string body = await createResponse.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.True(
            createResponse.StatusCode == HttpStatusCode.Created,
            $"Expected Created, got {createResponse.StatusCode}. Body: {body}");

        FormSummaryDto created = (await createResponse.Content
            .ReadFromJsonAsync<FormSummaryDto>()
            .ConfigureAwait(false))!;
        Assert.Equal(code, created.Code);
        Assert.NotNull(created.EditableVersionId);
        Assert.Equal("draft", created.EditableStatus);
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
