using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

using Cynara.Application.Failures;
using Cynara.Application.Persistence;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cynara.Api.Tests.Workflows;

/// <summary>
/// End-to-end proof that a write-write race surfacing at
/// <c>SaveChangesAsync</c> as a raw EF <c>DbUpdateConcurrencyException</c>
/// maps to HTTP 409 "Concurrency conflict" on both transports instead of a
/// 500, and never reaches the failure log. The fault-injected
/// <see cref="IUnitOfWork"/> throws exactly where EF would report zero
/// affected rows, past every pre-save guard.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class WorkflowConcurrencyConflictTests : IDisposable
{
    private const string Actor = "concurrency-tester";

    private readonly ConcurrencyFaultWebApplicationFactory factory;
    private readonly HttpClient client;
    private readonly JsonApiClient api;

    public WorkflowConcurrencyConflictTests(PostgreSqlDatabaseFixture database)
    {
        factory = new ConcurrencyFaultWebApplicationFactory(database.Settings);
        client = factory.CreateClient();
        client.AcceptJsonApi();
        api = new JsonApiClient(client);
        api.UseHospitalContext(factory.BootstrapOptions.BootstrapCode);
        api.UseActor(Actor);
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task JsonApiPatch_RaceAtSave_Returns409ConcurrencyConflict()
    {
        string definitionId = await api.CreateWorkflowDefinitionAsync(
            "conc-jsonapi",
            "conc-jsonapi",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);
        string draftId = await api.GetDraftVersionIdAsync(definitionId)
            .ConfigureAwait(false);
        uint rowVersion = await api.GetVersionRowVersionAsync(draftId)
            .ConfigureAwait(false);

        factory.Fault.Armed = true;
        using HttpResponseMessage response = await PatchWorkflowDraftAsync(
            draftId,
            rowVersion).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        (int status, string title) = await RequireFirstErrorAsync(response)
            .ConfigureAwait(false);
        Assert.Equal(409, status);
        Assert.Equal("Concurrency conflict", title);
        Assert.Empty(factory.FailureLog.Records);
    }

    [Fact]
    public async Task MinimalApiPipelineStart_RaceAtSave_Returns409Conflict()
    {
        _ = await api.CreateAndPublishWorkflowAsync(
            "conc-minimal",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);
        (_, Guid encounterId) = await api.SeedEncounterAsync()
            .ConfigureAwait(false);

        factory.Fault.Armed = true;
        (HttpStatusCode status, JsonDocument body) = await PostJsonAsync(
            "/api/pipelines",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["workflowCode"] = "conc-minimal",
                ["subjectType"] = "encounter",
                ["subjectId"] = encounterId,
            }).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Conflict, status);
        JsonElement error = body.RootElement.GetProperty("errors")[0];
        Assert.Equal("409", error.GetProperty("status").GetString());
        Assert.Equal(
            "Concurrency conflict",
            error.GetProperty("title").GetString());
        Assert.Empty(factory.FailureLog.Records);
    }

    private async Task<HttpResponseMessage> PatchWorkflowDraftAsync(
        string draftId,
        uint rowVersion)
    {
        string changedSchema = WorkflowTestSchemas.Minimal().Replace(
            "Workflow starts",
            "Workflow starts raced",
            StringComparison.Ordinal);
        var payload = new
        {
            data = new
            {
                type = "workflowVersions",
                id = draftId,
                attributes = new
                {
                    workflowSchemaJson = changedSchema,
                    rowVersion,
                },
            },
        };
        using StringContent content = JsonApiClient.CreateJsonApiContent(payload);
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            new Uri($"/api/workflowVersions/{draftId}", UriKind.Relative))
        {
            Content = content,
        };
        return await client.SendAsync(request).ConfigureAwait(false);
    }

    private static async Task<(int Status, string Title)> RequireFirstErrorAsync(
        HttpResponseMessage response)
    {
        string text = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        using var document = JsonDocument.Parse(text);
        JsonElement error = document.RootElement.GetProperty("errors")[0];
        return (
            int.Parse(
                error.GetProperty("status").GetString()!,
                CultureInfo.InvariantCulture),
            error.GetProperty("title").GetString()!);
    }

    private async Task<(HttpStatusCode Status, JsonDocument Body)> PostJsonAsync(
        string path,
        object body)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json"),
        };
        using HttpResponseMessage response = await client.SendAsync(request)
            .ConfigureAwait(false);
        HttpStatusCode status = response.StatusCode;
        string text = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        return (status, JsonDocument.Parse(
            string.IsNullOrWhiteSpace(text) ? "{}" : text));
    }
}

/// <summary>Arms the one-shot save fault for the next SaveChanges call.</summary>
internal sealed class ConcurrencyFaultSwitch
{
    public bool Armed { get; set; }
}

/// <summary>In-memory stand-in proving mapped conflicts skip the log.</summary>
internal sealed class RecordingFailureLogWriter : IFailureLogWriter
{
    public List<RecordedFailure> Records { get; } = [];

    public Task RecordAsync(
        Exception exception,
        FailureRequestContext context,
        int statusCode,
        CancellationToken cancellationToken)
    {
        Records.Add(new RecordedFailure(
            exception.GetType().Name,
            statusCode));
        return Task.CompletedTask;
    }

    public sealed record RecordedFailure(string ExceptionType, int StatusCode);
}

/// <summary>
/// Throws <see cref="DbUpdateConcurrencyException"/> right after the inner
/// save when armed, mimicking EF detecting a lost write-write race at flush
/// time — past every pre-save concurrency guard.
/// </summary>
internal sealed class FaultInjectionUnitOfWork(
    IUnitOfWork inner,
    ConcurrencyFaultSwitch fault) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        int affected = await inner.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        if (fault.Armed)
        {
            fault.Armed = false;
            throw new DbUpdateConcurrencyException(
                "The database operation was expected to affect 1 row(s), "
                + "but actually affected 0 row(s).");
        }

        return affected;
    }
}

internal sealed class ConcurrencyFaultWebApplicationFactory(
    TestDatabaseSettings database)
    : CynaraWebApplicationFactory(database)
{
    public ConcurrencyFaultSwitch Fault { get; } = new();

    public RecordingFailureLogWriter FailureLog { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IUnitOfWork>();
            services.AddSingleton(Fault);
            services.AddScoped<IUnitOfWork>(provider =>
                new FaultInjectionUnitOfWork(
                    provider.GetRequiredService<CynaraDbContext>(),
                    provider.GetRequiredService<ConcurrencyFaultSwitch>()));
            services.RemoveAll<IFailureLogWriter>();
            services.AddSingleton<IFailureLogWriter>(FailureLog);
        });
    }
}
