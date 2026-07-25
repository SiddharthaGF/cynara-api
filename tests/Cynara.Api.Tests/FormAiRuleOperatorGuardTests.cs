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

public sealed class FormAiRuleOperatorGuardTests : IDisposable
{
    private readonly GuardFactory factory = new();

    public void Dispose()
    {
        factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Chat_WithRegexValidation_RemovesBadValidationFromRules()
    {
        const string clinical = /*lang=json,strict*/ """
            {
              "schemaVersion":"1.0.0",
              "fields":[{"id":"cedula","code":"patient.cedula","type":"text","required":true}]
            }
            """;
        const string ui = /*lang=json,strict*/ """
            {"schemaVersion":"1.0.0","clinicalSchemaVersion":"1.0.0","fields":{"cedula":{"label":"Cédula","widget":"text-input"}},"layout":[]}
            """;
        const string rules = /*lang=json,strict*/ """
            {"schemaVersion":"1.0.0","clinicalSchemaVersion":"1.0.0","fields":{},"validations":[]}
            """;

        HttpClient client = factory.WithRegexValidationResponse().CreateClient();
        client.AcceptJsonApi();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            factory.BootstrapOptions.BootstrapCode ?? "default");
        string formId = await CreateFormDefinitionAsync(client, "ai-regex-guard")
            .ConfigureAwait(false);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/ai/forms/{formId}/chat",
            new
            {
                messages = new[]
                {
                    new { role = "user", content = "agregá una validación regex al campo cédula" },
                },
                locale = "es",
                clinicalSchemaJson = clinical,
                uiSchemaJson = ui,
                rulesSchemaJson = rules,
            }).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        string returnedRules = body.RootElement.GetProperty("rulesSchemaJson").GetString()!;
        using var returned = JsonDocument.Parse(returnedRules);
        JsonElement validations = returned.RootElement.GetProperty("validations");
        Assert.Equal(0, validations.GetArrayLength());
    }

    private static async Task<string> CreateFormDefinitionAsync(
        HttpClient client,
        string code)
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

    [Fact]
    public void Skill_LoaderReturnsCanonicalSkillBody()
    {
        IFormAiSkillLoader loader = factory.Services.GetRequiredService<IFormAiSkillLoader>();
        string body = loader.GetSkillBody();

        Assert.False(
            string.IsNullOrWhiteSpace(body),
            $"skill loader returned empty body. baseDir={AppContext.BaseDirectory} cwd={Directory.GetCurrentDirectory()}");
        Assert.Contains("form-schema-authoring", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Allowed ops", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`pattern`", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Clinical constraints", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Skill_LoaderIncludesEveryAssetBlockAsParseableJson()
    {
        IFormAiSkillLoader loader = factory.Services.GetRequiredService<IFormAiSkillLoader>();
        string body = loader.GetSkillBody();

        foreach (string name in (string[])["output-template.json", "widget-map.json", "rules-examples.json"])
        {
            string header = $"## assets/{name}";
            Assert.Contains(header, body, StringComparison.Ordinal);
            int headerIndex = body.IndexOf(header, StringComparison.Ordinal);
            Assert.True(headerIndex >= 0, $"missing {header}");
            int fenceStart = body.IndexOf("```json\n", headerIndex, StringComparison.Ordinal);
            Assert.True(fenceStart >= 0, $"missing json fence after {header}");
            int jsonStart = fenceStart + "```json\n".Length;
            int fenceEnd = body.IndexOf("\n```", jsonStart, StringComparison.Ordinal);
            Assert.True(fenceEnd > jsonStart, $"unterminated json fence for {header}");
            string json = body[jsonStart..fenceEnd];
            try
            {
                using var doc = JsonDocument.Parse(json);
            }
            catch (JsonException ex)
            {
                throw new Xunit.Sdk.XunitException($"asset {name} is not valid JSON: {ex.Message}");
            }
        }
    }
}

internal sealed class GuardFactory : CynaraWebApplicationFactory
{
    private RegexOpenAiClientStub? stubOverride;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
(StringComparer.Ordinal)
            {
                ["OPENAI_API_KEY"] = "test-api-key",
                ["OPENAI_BASE_URL"] = "https://example.test/v1",
                ["OPENAI_MODEL"] = "test-model",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IOpenAiClient>();
            services.AddSingleton<IOpenAiClient>(_ =>
                stubOverride ?? new RegexOpenAiClientStub(emitRegexValidation: false));
        });
    }

    public GuardFactory WithRegexValidationResponse()
    {
        stubOverride = new RegexOpenAiClientStub(emitRegexValidation: true);
        return this;
    }
}
