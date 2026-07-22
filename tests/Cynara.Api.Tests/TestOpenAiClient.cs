using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

using Cynara.Application.Modules.FormAi;

namespace Cynara.Api.Tests;

internal sealed class TestOpenAiClient : IOpenAiClient
{
    private const int StreamChunkSize = 48;

    public Task<OpenAiCompletionResult> CreateChatCompletionAsync(
        IReadOnlyList<OpenAiMessage> messages,
        OpenAiConfig config,
        string? cacheScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(config);
        cancellationToken.ThrowIfCancellationRequested();

        string content = CreateResponse(messages);
        return Task.FromResult(new OpenAiCompletionResult(content, null));
    }

    public IAsyncEnumerable<OpenAiStreamDelta> StreamChatCompletionAsync(
        IReadOnlyList<OpenAiMessage> messages,
        OpenAiConfig config,
        string? cacheScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(config);

        return StreamChatCompletionIterator(messages, cancellationToken);
    }

    private static async IAsyncEnumerable<OpenAiStreamDelta> StreamChatCompletionIterator(
        IReadOnlyList<OpenAiMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string content = CreateResponse(messages);
        for (int offset = 0; offset < content.Length; offset += StreamChunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new OpenAiStreamDelta(
                offset + StreamChunkSize >= content.Length
                    ? content[offset..]
                    : content.Substring(offset, StreamChunkSize),
                null);
        }
    }

    private static string CreateResponse(IReadOnlyList<OpenAiMessage> messages)
    {
        JsonObject turn = ParseUserTurn(messages);
        string locale = turn["locale"]?.GetValue<string>() ?? "en";
        bool spanish = locale.StartsWith("es", StringComparison.OrdinalIgnoreCase);
        string fieldId = NextFieldId(turn);
        string label = spanish ? "Pregunta de prueba" : "Test question";

        return new JsonObject
        {
            ["summary"] = spanish
                ? "Pregunta de prueba añadida"
                : "Test question added",
            ["assistantMessage"] = spanish
                ? "Añadí una pregunta de texto para probar el formulario."
                : "I added a text question to test the form.",
            ["mode"] = "patch",
            ["patch"] = new JsonObject
            {
                ["upsertClinicalFields"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = fieldId,
                        ["code"] = $"test.{fieldId}",
                        ["type"] = "text",
                    },
                },
                ["upsertUiFields"] = new JsonObject
                {
                    [fieldId] = new JsonObject
                    {
                        ["label"] = label,
                        ["widget"] = "text-input",
                    },
                },
                ["layout"] = BuildLayout(turn, fieldId),
            },
        }.ToJsonString();
    }

    private static JsonArray BuildLayout(JsonObject turn, string fieldId)
    {
        JsonArray layout = turn["currentDraft"]?["ui"]?["layout"]
            is JsonArray currentLayout
            ? (JsonArray)currentLayout.DeepClone()
            : [];
        layout.Add(new JsonObject
        {
            ["type"] = "field",
            ["fieldId"] = fieldId,
        });
        return layout;
    }

    private static JsonObject ParseUserTurn(
        IReadOnlyList<OpenAiMessage> messages)
    {
        string? content = messages.LastOrDefault(message =>
            string.Equals(
                message.Role,
                "user",
                StringComparison.OrdinalIgnoreCase))?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(content) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string NextFieldId(JsonObject turn)
    {
        var existingIds = new HashSet<string>(StringComparer.Ordinal);
        if (turn["currentDraft"]?["clinical"]?["fields"] is JsonArray fields)
        {
            foreach (JsonNode? field in fields)
            {
                string? id = field?["id"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    _ = existingIds.Add(id);
                }
            }
        }

        const string baseId = "test-question";
        if (!existingIds.Contains(baseId))
        {
            return baseId;
        }

        for (int suffix = 2; ; suffix++)
        {
            string candidate = $"{baseId}-{suffix}";
            if (!existingIds.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
