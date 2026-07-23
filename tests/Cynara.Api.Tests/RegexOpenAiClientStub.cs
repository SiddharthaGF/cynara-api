using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

using Cynara.Application.Modules.FormAi;

namespace Cynara.Api.Tests;

internal sealed class RegexOpenAiClientStub : IOpenAiClient
{
    private readonly bool emitRegexValidation;

    public RegexOpenAiClientStub(bool emitRegexValidation)
    {
        this.emitRegexValidation = emitRegexValidation;
    }

    public Task<OpenAiCompletionResult> CreateChatCompletionAsync(
        IReadOnlyList<OpenAiMessage> messages,
        OpenAiConfig config,
        string? cacheScope,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new OpenAiCompletionResult(CreateResponse(), Thinking: null));
    }

    public IAsyncEnumerable<OpenAiStreamDelta> StreamChatCompletionAsync(
        IReadOnlyList<OpenAiMessage> messages,
        OpenAiConfig config,
        string? cacheScope,
        CancellationToken cancellationToken)
    {
        return StreamAsync(cancellationToken);
    }

    private async IAsyncEnumerable<OpenAiStreamDelta> StreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        yield return new OpenAiStreamDelta(CreateResponse(), Reasoning: null);
    }

    private string CreateResponse()
    {
        JsonObject regexValidation = new()
        {
            ["code"] = "CEDULA_REGEX",
            ["message"] = "Debe ser 8 dígitos",
            ["assert"] = new JsonObject
            {
                ["op"] = "regex",
                ["args"] = new JsonArray
                {
                    new JsonObject { ["ref"] = "patient.cedula" },
                    new JsonObject { ["lit"] = "^\\d{8}$" },
                },
            },
        };

        JsonArray upsertValidations = [];
        if (emitRegexValidation)
        {
            upsertValidations.Add(regexValidation);
        }

        JsonObject patch = new()
        {
            ["upsertClinicalFields"] = new JsonArray(),
            ["removeFieldIds"] = new JsonArray(),
            ["upsertUiFields"] = new JsonObject(),
            ["layout"] = new JsonArray(),
            ["upsertRulesFields"] = new JsonObject(),
            ["removeRulesFieldIds"] = new JsonArray(),
            ["upsertValidations"] = upsertValidations,
            ["removeValidationCodes"] = new JsonArray(),
            ["clear"] = false,
        };

        return new JsonObject
        {
            ["summary"] = "stub",
            ["assistantMessage"] = "stub",
            ["mode"] = "patch",
            ["patch"] = patch,
        }.ToJsonString();
    }
}
