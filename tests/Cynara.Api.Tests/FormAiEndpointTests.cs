using System.Net.Http.Json;
using System.Text.Json;

using Cynara.Api.Tests.Support;
using Cynara.Application.Modules.FormAi;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cynara.Api.Tests;

public sealed class FormAiEndpointTests : IDisposable
{
    private readonly FormAiWebApplicationFactory factory = new();
    private readonly HttpClient client;

    public FormAiEndpointTests()
    {
        client = factory.CreateClient();
        client.AcceptJsonApi();
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
    public async Task Status_ReturnsConfiguredEnvironmentWithoutSecrets()
    {
        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/ai/status", UriKind.Relative)).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.True(body.RootElement.GetProperty("configured").GetBoolean());
        Assert.Equal(
            "test-model",
            body.RootElement.GetProperty("model").GetString());
        Assert.Equal("env", body.RootElement.GetProperty("source").GetString());
        Assert.True(
            body.RootElement.GetProperty("apiKeyConfigured").GetBoolean());
    }

    [Fact]
    public async Task PlainJsonSettingsRoutes_AreRemoved()
    {
        using HttpResponseMessage get = await client.GetAsync(
            new Uri("/api/ai/settings", UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, get.StatusCode);

        using HttpResponseMessage put = await client.PutAsJsonAsync(
            "/api/ai/settings",
            new { baseUrl = "https://api.openai.com/v1", model = "gpt-4o-mini" })
            .ConfigureAwait(false);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, put.StatusCode);
    }

    [Fact]
    public async Task AiProviderSettings_GetDefault_ProjectsPublicView()
    {
        var api = new JsonApiClient(client);
        using JsonDocument document = await api.GetAsync(
            "/api/aiProviderSettings/default").ConfigureAwait(false);

        Assert.Equal("default", JsonApiClient.RequireId(document));
        Assert.Equal("env", JsonApiClient.AttrString(document, "source"));
        Assert.True(
            document.RootElement
                .GetProperty("data")
                .GetProperty("attributes")
                .GetProperty("configured")
                .GetBoolean());
        Assert.True(
            document.RootElement
                .GetProperty("data")
                .GetProperty("attributes")
                .GetProperty("hasApiKey")
                .GetBoolean());
        Assert.True(
            document.RootElement
                .GetProperty("data")
                .GetProperty("attributes")
                .TryGetProperty("suggestions", out JsonElement suggestions));
        Assert.True(suggestions.GetArrayLength() > 0);
        Assert.DoesNotContain(
            "test-api-key",
            document.RootElement.GetRawText(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AiProviderSettings_PatchRequiresApiKeyWithoutStoredProvider()
    {
        using StringContent content = JsonApiClient.CreateJsonApiContent(new
        {
            data = new
            {
                type = "aiProviderSettings",
                id = "default",
                attributes = new
                {
                    baseUrl = "https://api.openai.com/v1",
                    model = "gpt-4o-mini",
                },
            },
        });
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            new Uri("/api/aiProviderSettings/default", UriKind.Relative))
        {
            Content = content,
        };
        using HttpResponseMessage response = await client
            .SendAsync(request)
            .ConfigureAwait(false);

        Assert.Equal(
            System.Net.HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task AiProviderSettings_PatchUpsertsAndClearApiKey()
    {
        var api = new JsonApiClient(client);
        using JsonDocument upserted = await api.PatchResourceAsync(
            "aiProviderSettings",
            "default",
            new
            {
                baseUrl = "https://api.openai.com/v1",
                model = "gpt-4o-mini",
                jsonObject = true,
                apiKey = "sk-test-upsert-key",
            }).ConfigureAwait(false);

        Assert.Equal("database", JsonApiClient.AttrString(upserted, "source"));
        Assert.True(
            upserted.RootElement
                .GetProperty("data")
                .GetProperty("attributes")
                .GetProperty("hasApiKey")
                .GetBoolean());
        Assert.Equal(
            "gpt-4o-mini",
            JsonApiClient.AttrString(upserted, "model"));
        Assert.DoesNotContain(
            "sk-test-upsert-key",
            upserted.RootElement.GetRawText(),
            StringComparison.Ordinal);

        using JsonDocument cleared = await api.PatchResourceAsync(
            "aiProviderSettings",
            "default",
            new
            {
                clearApiKey = true,
                baseUrl = "https://api.openai.com/v1",
                model = "gpt-4o-mini",
            }).ConfigureAwait(false);

        // DB secret cleared; factory env key becomes the active source.
        Assert.Equal("env", JsonApiClient.AttrString(cleared, "source"));
        Assert.DoesNotContain(
            "sk-test-upsert-key",
            cleared.RootElement.GetRawText(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chat_TestProviderAddsDeterministicTextQuestion()
    {
        string formId = await CreateFormDefinitionAsync("ai-chat-demo").ConfigureAwait(false);
        const string clinical = /*lang=json,strict*/ """
            {"schemaVersion":"1.0.0","fields":[]}
            """;
        const string ui = /*lang=json,strict*/ """
            {"schemaVersion":"1.0.0","clinicalSchemaVersion":"1.0.0","fields":{},"layout":[]}
            """;
        const string rules = /*lang=json,strict*/ """
            {"schemaVersion":"1.0.0","clinicalSchemaVersion":"1.0.0","fields":{},"validations":[]}
            """;

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/ai/forms/{formId}/chat",
            new
            {
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = "Agrega una pregunta para probar el formulario",
                    },
                },
                locale = "es",
                clinicalSchemaJson = clinical,
                uiSchemaJson = ui,
                rulesSchemaJson = rules,
            }).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        using var updatedClinical = JsonDocument.Parse(
            body.RootElement.GetProperty("clinicalSchemaJson").GetString()!);
        JsonElement field = updatedClinical.RootElement
            .GetProperty("fields")[0];
        Assert.Equal("test-question", field.GetProperty("id").GetString());
        Assert.Equal("text", field.GetProperty("type").GetString());
        Assert.Contains(
            "probar",
            body.RootElement.GetProperty("assistantMessage").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChatStream_TestProviderReturnsDoneEvent()
    {
        string formId = await CreateFormDefinitionAsync("ai-chat-stream").ConfigureAwait(false);
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
                clinicalSchemaJson = /*lang=json,strict*/
                    "{\"schemaVersion\":\"1.0.0\",\"fields\":[]}",
                uiSchemaJson = /*lang=json,strict*/
                    "{\"schemaVersion\":\"1.0.0\",\"clinicalSchemaVersion\":\"1.0.0\",\"fields\":{},\"layout\":[]}",
                rulesSchemaJson = /*lang=json,strict*/
                    "{\"schemaVersion\":\"1.0.0\",\"clinicalSchemaVersion\":\"1.0.0\",\"fields\":{},\"validations\":[]}",
            }).ConfigureAwait(false);

        string body = await response.Content
            .ReadAsStringAsync().ConfigureAwait(false);
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
        Assert.Equal(
            "text/event-stream",
            response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"type\":\"message\"", body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"done\"", body, StringComparison.Ordinal);
        Assert.Contains("test-question", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chat_GuardrailReturnsUnchangedDraftWithoutProviderCall()
    {
        string formId = await CreateFormDefinitionAsync("ai-guardrail").ConfigureAwait(false);
        const string clinical = /*lang=json,strict*/ """
            {"schemaVersion":"1.0.0","fields":[{"id":"name","code":"patient.name","type":"text"}]}
            """;
        const string ui = /*lang=json,strict*/ """
            {"schemaVersion":"1.0.0","clinicalSchemaVersion":"1.0.0","fields":{"name":{"label":"Name","widget":"text-input"}}}
            """;
        const string rules = /*lang=json,strict*/ """
            {"schemaVersion":"1.0.0","clinicalSchemaVersion":"1.0.0","fields":{},"validations":[]}
            """;

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/ai/forms/{formId}/chat",
            new
            {
                messages = new[] { new { role = "user", content = "Search the web for this" } },
                locale = "en",
                clinicalSchemaJson = clinical,
                uiSchemaJson = ui,
                rulesSchemaJson = rules,
            }).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Contains(
            "internet",
            body.RootElement.GetProperty("assistantMessage").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            clinical,
            body.RootElement.GetProperty("clinicalSchemaJson").GetString());
    }

    [Fact]
    public async Task Chat_ClearAllFieldsReturnsEmptyFieldsArray()
    {
        string formId = await CreateFormDefinitionAsync("ai-clear-fields").ConfigureAwait(false);
        const string clinical = /*lang=json,strict*/ """
            {"schemaVersion":"1.0.0","fields":[{"id":"name","code":"patient.name","type":"text"}]}
            """;
        const string ui = /*lang=json,strict*/ """
            {"schemaVersion":"1.0.0","clinicalSchemaVersion":"1.0.0","fields":{"name":{"label":"Name","widget":"text-input"}},"layout":[]}
            """;
        const string rules = /*lang=json,strict*/ """
            {"schemaVersion":"1.0.0","clinicalSchemaVersion":"1.0.0","fields":{},"validations":[]}
            """;

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/ai/forms/{formId}/chat",
            new
            {
                messages = new[] { new { role = "user", content = "Clear all fields" } },
                locale = "en",
                clinicalSchemaJson = clinical,
                uiSchemaJson = ui,
                rulesSchemaJson = rules,
            }).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        using var updatedClinical = JsonDocument.Parse(
            body.RootElement.GetProperty("clinicalSchemaJson").GetString()!);
        Assert.Equal(
            0,
            updatedClinical.RootElement.GetProperty("fields").GetArrayLength());
        Assert.Contains(
            "cleared",
            body.RootElement.GetProperty("assistantMessage").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChatStream_GuardrailReturnsDoneEvent()
    {
        string formId = await CreateFormDefinitionAsync("ai-guardrail-stream")
            .ConfigureAwait(false);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/ai/forms/{formId}/chat/stream",
            new
            {
                messages = new[] { new { role = "user", content = "Tell me a joke" } },
                clinicalSchemaJson = /*lang=json,strict*/
                    "{\"schemaVersion\":\"1.0.0\",\"fields\":[{\"id\":\"name\",\"code\":\"patient.name\",\"type\":\"text\"}]}",
            }).ConfigureAwait(false);

        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
        Assert.Equal(
            "text/event-stream",
            response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"type\":\"done\"", body, StringComparison.Ordinal);
    }

    private async Task<string> CreateFormDefinitionAsync(string code)
    {
        var api = new JsonApiClient(client);
        using JsonDocument created = await api.PostResourceAsync(
            "formDefinitions",
            new
            {
                code,
                name = code,
                initialClinicalSchemaJson =
                    JsonApiWorkflow.MinimalClinicalSchema("notes", "form.notes"),
            }).ConfigureAwait(false);
        return JsonApiClient.RequireId(created);
    }
}

internal sealed class FormAiWebApplicationFactory : CynaraWebApplicationFactory
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
            services.AddSingleton<IOpenAiClient, TestOpenAiClient>();
        });
    }
}
