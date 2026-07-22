using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;

using Xunit;

namespace Cynara.Api.Tests;

public sealed class FormAiEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public FormAiEndpointTests(WebApplicationFactory<Program> factory)
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task Status_ReturnsMockProviderWithoutSecrets()
    {
        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/ai/status", UriKind.Relative)).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.True(body.RootElement.GetProperty("configured").GetBoolean());
        Assert.Equal(
            "mock-form-ai",
            body.RootElement.GetProperty("model").GetString());
        Assert.Equal("mock", body.RootElement.GetProperty("source").GetString());
        Assert.False(
            body.RootElement.GetProperty("apiKeyConfigured").GetBoolean());
    }

    [Fact]
    public async Task Settings_DoNotRequireApiKeyWithMockProvider()
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            "/api/ai/settings",
            new
            {
                baseUrl = "https://api.openai.com/v1",
                model = "gpt-4o-mini",
            }).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Equal("mock", body.RootElement.GetProperty("source").GetString());
    }

    [Fact]
    public async Task Chat_MockProviderAddsDeterministicTextQuestion()
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
        Assert.Equal("mock-question", field.GetProperty("id").GetString());
        Assert.Equal("text", field.GetProperty("type").GetString());
        Assert.Contains(
            "simulada",
            body.RootElement.GetProperty("assistantMessage").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChatStream_MockProviderReturnsDoneEvent()
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
                        content = "Add a question for the mock form",
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
        Assert.Contains("mock-question", body, StringComparison.Ordinal);
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
                clinicalSchemaJson = /*lang=json,strict*/ "{\"schemaVersion\":\"1.0.0\",\"fields\":[{\"id\":\"name\",\"code\":\"patient.name\",\"type\":\"text\"}]}"
            }).ConfigureAwait(false);

        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"type\":\"done\"", body, StringComparison.Ordinal);
    }
}
