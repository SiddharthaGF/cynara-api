using System.Net.Http.Json;
using System.Text.Json;

using Cynara.Application.Modules.FormAi;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Xunit;

namespace Cynara.Api.Tests;

public sealed class FormAiEndpointTests : IDisposable
{
    private readonly FormAiWebApplicationFactory factory = new();
    private readonly HttpClient client;

    public FormAiEndpointTests()
    {
        client = factory.CreateClient();
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
    public async Task Settings_RequiresApiKeyWithoutStoredProvider()
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            "/api/ai/settings",
            new
            {
                baseUrl = "https://api.openai.com/v1",
                model = "gpt-4o-mini",
            }).ConfigureAwait(false);

        Assert.Equal(
            System.Net.HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task Chat_TestProviderAddsDeterministicTextQuestion()
    {
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
            "/api/forms/demo/draft/ai-chat",
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
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/forms/demo/draft/ai-chat/stream",
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
            "/api/forms/demo/draft/ai-chat",
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
        Assert.Contains("internet", body.RootElement.GetProperty("assistantMessage").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(clinical, body.RootElement.GetProperty("clinicalSchemaJson").GetString());
    }

    [Fact]
    public async Task ChatStream_GuardrailReturnsDoneEvent()
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/forms/demo/draft/ai-chat/stream",
            new
            {
                messages = new[] { new { role = "user", content = "Tell me a joke" } },
                clinicalSchemaJson = /*lang=json,strict*/ "{\"schemaVersion\":\"1.0.0\",\"fields\":[{\"id\":\"name\",\"code\":\"patient.name\",\"type\":\"text\"}]}",
            }).ConfigureAwait(false);

        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"type\":\"done\"", body, StringComparison.Ordinal);
    }
}

internal sealed class FormAiWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
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
