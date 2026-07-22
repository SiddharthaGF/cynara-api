using System.Net;
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
    public async Task Status_ReturnsUnconfiguredProviderWithoutSecrets()
    {
        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/ai/status", UriKind.Relative)).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.False(body.RootElement.GetProperty("configured").GetBoolean());
        Assert.Equal("none", body.RootElement.GetProperty("source").GetString());
    }

    [Fact]
    public async Task Settings_RequireApiKeyWhenPersistingProvider()
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            "/api/ai/settings",
            new
            {
                baseUrl = "https://api.openai.com/v1",
                model = "gpt-4o-mini",
            }).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
