using System.Net.Http.Json;

using Cynara.Api.Tests.Support;
using Cynara.Application.Modules.FormAi;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cynara.Api.Tests;

[Collection(PostgresFixtureDefinition.Name)]
public sealed class FormAiTruncationTests : IDisposable
{
    private const string Clinical = /*lang=json,strict*/
        """{"schemaVersion":"1.0.0","fields":[]}""";

    private const string Ui = /*lang=json,strict*/
        """{"schemaVersion":"1.0.0","clinicalSchemaVersion":"1.0.0","fields":{},"layout":[]}""";

    private const string Rules = /*lang=json,strict*/
        """{"schemaVersion":"1.0.0","clinicalSchemaVersion":"1.0.0","fields":{},"validations":[]}""";

    private readonly TruncatedFormAiWebApplicationFactory factory;
    private readonly HttpClient client;

    public FormAiTruncationTests(PostgreSqlDatabaseFixture database)
    {
        factory = new TruncatedFormAiWebApplicationFactory(database.Settings);
        client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            factory.BootstrapOptions.BootstrapCode ?? "default");
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ChatStream_WhenProviderReportsLength_DoesNotEmitDone()
    {
        var api = new JsonApiClient(client);
        using System.Text.Json.JsonDocument form = await api.PostResourceAsync(
            "formDefinitions",
            new
            {
                code = "ai-truncated",
                name = "ai-truncated",
                initialClinicalSchemaJson =
                    JsonApiWorkflow.MinimalClinicalSchema("notes", "form.notes"),
            }).ConfigureAwait(false);
        string formId = JsonApiClient.RequireId(form);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/ai/forms/{formId}/chat/stream",
            new
            {
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = "Add a question for the test form",
                    },
                },
                locale = "en",
                clinicalSchemaJson = Clinical,
                uiSchemaJson = Ui,
                rulesSchemaJson = Rules,
            }).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.True(response.IsSuccessStatusCode, body);
        Assert.Contains("\"type\":\"error\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"type\":\"done\"", body, StringComparison.Ordinal);
        Assert.Contains("truncated", body, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class TruncatedFormAiWebApplicationFactory(TestDatabaseSettings database)
    : CynaraWebApplicationFactory(database)
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
                (StringComparer.Ordinal)
            {
                ["CYNARA_ENV"] = "preview",
                ["OPENAI_API_KEY"] = "test-api-key",
                ["OPENAI_BASE_URL"] = "https://example.test/v1",
                ["OPENAI_MODEL"] = "test-model",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IOpenAiClient>();
            services.AddSingleton<IOpenAiClient, TruncatedOpenAiClient>();
        });
    }
}

internal sealed class TruncatedOpenAiClient : IOpenAiClient
{
    private const string IncompleteJson =
        "{\"summary\":\"Updated\",\"assistantMessage\":\"I started\",\"mode\":\"patch\",\"patch\":{";

    public Task<OpenAiCompletionResult> CreateChatCompletionAsync(
        IReadOnlyList<OpenAiMessage> messages,
        OpenAiConfig config,
        string? cacheScope,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(
            new OpenAiCompletionResult(
                IncompleteJson,
                Thinking: null,
                IsTruncated: true));
    }

    public async IAsyncEnumerable<OpenAiStreamDelta> StreamChatCompletionAsync(
        IReadOnlyList<OpenAiMessage> messages,
        OpenAiConfig config,
        string? cacheScope,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        yield return new OpenAiStreamDelta(
            IncompleteJson,
            Reasoning: null,
            IsTruncated: true);
    }
}
