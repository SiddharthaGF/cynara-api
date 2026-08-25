using System.Text.Json;

using Cynara.Application.Common;
using Cynara.Domain.Audit;

namespace Cynara.Api.Tests.FormAi;

/// <summary>
/// Audit contract for AI provider settings mutations: every upsert (set or
/// clear the API key) persists exactly one <c>ai-provider-settings.updated</c>
/// event scoped to the hospital and attributed to the acting actor, and the
/// canonical metadata records only boolean key facts — never the raw API key
/// value.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class FormAiSettingsAuditTests : IDisposable
{
    private const string Actor = "ai-settings-auditor";
    private const string SetKey = "sk-audit-secret-set-1234";
    private const string ReplaceKey = "sk-audit-secret-replace-5678";

    private readonly FormAiWebApplicationFactory factory;
    private readonly HttpClient client;
    private readonly JsonApiClient api;

    public FormAiSettingsAuditTests(PostgreSqlDatabaseFixture database)
    {
        factory = new FormAiWebApplicationFactory(database.Settings);
        client = factory.CreateClient();
        client.AcceptJsonApi();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            factory.BootstrapOptions.BootstrapCode ?? "default");
        client.DefaultRequestHeaders.Add("X-Actor-Id", Actor);
        api = new JsonApiClient(client);
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task UpsertWithApiKey_EmitsAuditEventWithoutRawKey()
    {
        using JsonDocument upserted = await api.PatchResourceAsync(
            "aiProviderSettings",
            "default",
            new
            {
                baseUrl = "https://api.openai.com/v1",
                model = "gpt-4o-mini",
                jsonObject = true,
                apiKey = SetKey,
            }).ConfigureAwait(false);

        Assert.Equal(
            "database",
            JsonApiClient.AttrString(upserted, "source"));

        AuditEvent auditEvent = await RequireLatestEventAsync()
            .ConfigureAwait(false);

        Assert.Equal(Actor, auditEvent.ActorId);
        Assert.Equal(Guid.Empty, auditEvent.ResourceId);
        Assert.Equal(
            await RequireHospitalIdAsync().ConfigureAwait(false),
            auditEvent.HospitalId);

        string metadata = auditEvent.MetadataJson ?? string.Empty;
        Assert.Contains("\"apiKeySet\":true", metadata, StringComparison.Ordinal);
        Assert.Contains(
            "\"apiKeyCleared\":false",
            metadata,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"baseUrl\":\"https://api.openai.com/v1\"",
            metadata,
            StringComparison.Ordinal);
        Assert.Contains("\"model\":\"gpt-4o-mini\"", metadata, StringComparison.Ordinal);
        Assert.DoesNotContain(SetKey, metadata, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClearApiKey_EmitsClearedAuditEventWithoutRawKey()
    {
        _ = await api.PatchResourceAsync(
            "aiProviderSettings",
            "default",
            new
            {
                baseUrl = "https://api.openai.com/v1",
                model = "gpt-4o-mini",
                apiKey = ReplaceKey,
            }).ConfigureAwait(false);

        using JsonDocument cleared = await api.PatchResourceAsync(
            "aiProviderSettings",
            "default",
            new
            {
                clearApiKey = true,
                baseUrl = "https://api.openai.com/v1",
                model = "gpt-4o-mini",
            }).ConfigureAwait(false);

        Assert.Equal("env", JsonApiClient.AttrString(cleared, "source"));

        AuditEvent auditEvent = await RequireLatestEventAsync()
            .ConfigureAwait(false);

        Assert.Equal(Actor, auditEvent.ActorId);

        string metadata = auditEvent.MetadataJson ?? string.Empty;
        Assert.Contains(
            "\"apiKeySet\":false",
            metadata,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"apiKeyCleared\":true",
            metadata,
            StringComparison.Ordinal);
        Assert.DoesNotContain(ReplaceKey, metadata, StringComparison.Ordinal);
    }

    /// <summary>
    /// The actor header isolates this class's events from settings upserts
    /// performed by other tests; within the class the latest event is always
    /// the one asserted on (the clear test's arrange patch precedes it).
    /// </summary>
    private async Task<AuditEvent> RequireLatestEventAsync()
    {
        await using AsyncServiceScope scope = factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        return await dbContext.AuditEvents
            .AsNoTracking()
            .Where(item => item.ResourceType
                == AuditEntityTypes.AiProviderSettings
                && item.Action == "ai-provider-settings.updated"
                && item.ActorId == Actor)
            .OrderByDescending(item => item.OccurredAt)
            .FirstAsync()
            .ConfigureAwait(false);
    }

    private async Task<Guid> RequireHospitalIdAsync()
    {
        string code = factory.BootstrapOptions.BootstrapCode ?? "default";
        await using AsyncServiceScope scope = factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        return await dbContext.Hospitals
            .AsNoTracking()
            .Where(item => item.Code == code)
            .Select(item => item.Id)
            .SingleAsync()
            .ConfigureAwait(false);
    }
}
