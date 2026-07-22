using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

using Cynara.Application;
using Cynara.Application.Modules.FormAi;

namespace Cynara.Infrastructure.Modules.FormAi;

public sealed class OpenAiClient : IOpenAiClient
{
    private readonly HttpClient httpClient;

    public OpenAiClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        this.httpClient = httpClient;
    }

    public async Task<OpenAiCompletionResult> CreateChatCompletionAsync(
        IReadOnlyList<OpenAiMessage> messages,
        OpenAiConfig config,
        string? cacheScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(config);
        EnsureConfigured(config);
        using HttpRequestMessage request = CreateRequest(messages, config, stream: false, cacheScope);
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new ValidationException(ReadProviderError(body, response.StatusCode));
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            JsonElement message = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message");
            string content = message.TryGetProperty("content", out JsonElement contentElement)
                ? contentElement.GetString() ?? string.Empty
                : string.Empty;
            string? thinking = ReadThinking(message);
            return SplitContentAndThinking(content, thinking);
        }
        catch (KeyNotFoundException)
        {
            throw new ValidationException(
                "OpenAI-compatible provider returned an invalid response.");
        }
        catch (JsonException)
        {
            throw new ValidationException(
                $"OpenAI-compatible provider returned non-JSON (HTTP {(int)response.StatusCode}).");
        }
    }

    public IAsyncEnumerable<OpenAiStreamDelta> StreamChatCompletionAsync(
        IReadOnlyList<OpenAiMessage> messages,
        OpenAiConfig config,
        string? cacheScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(config);
        EnsureConfigured(config);
        return StreamCoreAsync(httpClient, messages, config, cacheScope, cancellationToken);

        static async IAsyncEnumerable<OpenAiStreamDelta> StreamCoreAsync(
            HttpClient client,
            IReadOnlyList<OpenAiMessage> msgs,
            OpenAiConfig cfg,
            string? cacheScope,
            [EnumeratorCancellation] CancellationToken ct)
        {
            using HttpRequestMessage request = CreateRequest(msgs, cfg, stream: true, cacheScope);
            using HttpResponseMessage response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new ValidationException(ReadProviderError(errorBody, response.StatusCode));
            }

            Stream stream = await response.Content
                .ReadAsStreamAsync(ct)
                .ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                using var reader = new StreamReader(stream);
                bool emittedAny = false;
                while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
                {
                    if (ct.IsCancellationRequested)
                    {
                        yield break;
                    }

                    string trimmed = line.Trim();
                    if (!trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string data = trimmed[5..].Trim();
                    if (data == "[DONE]")
                    {
                        continue;
                    }

                    JsonDocument document;
                    try
                    {
                        document = JsonDocument.Parse(data);
                    }
                    catch (JsonException)
                    {
                        continue;
                    }

                    using (document)
                    {
                        JsonElement root = document.RootElement;
                        if (root.TryGetProperty("error", out JsonElement error)
                            && error.TryGetProperty("message", out JsonElement errorMessage))
                        {
                            throw new ValidationException(
                                errorMessage.GetString() ?? "AI stream failed.");
                        }

                        JsonElement delta;
                        try
                        {
                            delta = root.GetProperty("choices")[0].GetProperty("delta");
                        }
                        catch (KeyNotFoundException)
                        {
                            continue;
                        }

                        string? content = ReadStringProperty(delta, "content");
                        string? reasoning = ReadStringProperty(delta, "reasoning_content")
                            ?? ReadStringProperty(delta, "reasoning");
                        if (!string.IsNullOrEmpty(content) || !string.IsNullOrEmpty(reasoning))
                        {
                            emittedAny = true;
                            yield return new OpenAiStreamDelta(content, reasoning);
                        }
                    }
                }

                if (!emittedAny)
                {
                    throw new ValidationException(
                        "OpenAI-compatible provider returned an empty assistant message.");
                }
            }
        }
    }

    private static HttpRequestMessage CreateRequest(
        IReadOnlyList<OpenAiMessage> messages,
        OpenAiConfig config,
        bool stream,
        string? cacheScope)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = config.Model,
            ["temperature"] = 1,
            ["messages"] = messages,
            ["reasoning_split"] = true,
        };
        if (stream)
        {
            body["stream"] = true;
        }

        if (config.JsonObject)
        {
            body["response_format"] = new { type = "json_object" };
        }

        ApplyPromptCache(body, messages, config, cacheScope);

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{config.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            config.ApiKey);
        if (config.BaseUrl.Contains("openrouter", StringComparison.OrdinalIgnoreCase))
        {
            _ = request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://cynara.app");
            _ = request.Headers.TryAddWithoutValidation("X-Title", "Cynara");
        }

        if (stream)
        {
            request.Headers.Accept.ParseAdd("text/event-stream");
        }

        return request;
    }

    private enum PromptCacheMode
    {
        None,
        AnthropicEphemeral,
        OpenAiPromptKey,
    }

    private static PromptCacheMode DetectPromptCacheMode(string baseUrl)
    {
        string host = baseUrl.ToLowerInvariant();
        if (host.Contains("api.anthropic.com", StringComparison.OrdinalIgnoreCase)
            || host.Contains("anthropic.com", StringComparison.OrdinalIgnoreCase))
        {
            return PromptCacheMode.AnthropicEphemeral;
        }
        if (host.Contains("api.openai.com", StringComparison.OrdinalIgnoreCase)
            || host.Contains("openrouter.ai", StringComparison.OrdinalIgnoreCase))
        {
            return PromptCacheMode.OpenAiPromptKey;
        }
        return PromptCacheMode.None;
    }

    private static string BuildPromptCacheKey(string scope)
    {
        string lowered = scope.Trim().ToLowerInvariant();
        var sb = new StringBuilder(lowered.Length);
        foreach (char c in lowered)
        {
            _ = sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-');
        }
        string key = "cynara:" + sb;
        return key.Length > 64 ? key[..64] : key;
    }

    private static void ApplyPromptCache(
        Dictionary<string, object?> body,
        IReadOnlyList<OpenAiMessage> messages,
        OpenAiConfig config,
        string? cacheScope)
    {
        if (string.IsNullOrWhiteSpace(cacheScope))
        {
            return;
        }
        PromptCacheMode mode = DetectPromptCacheMode(config.BaseUrl);
        if (mode == PromptCacheMode.None)
        {
            return;
        }
        string key = BuildPromptCacheKey(cacheScope);
        if (mode == PromptCacheMode.OpenAiPromptKey)
        {
            body["prompt_cache_key"] = key;
            return;
        }
        // Anthropic: attach a cache_control breakpoint to the system prompt.
        // Re-shape the messages list with the breakpoint on the last system entry.
        var annotated = new List<OpenAiMessage>(messages.Count);
        bool attached = false;
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            if (!attached
                && string.Equals(messages[i].Role, "system", StringComparison.OrdinalIgnoreCase))
            {
                annotated.Insert(
                    0,
                    messages[i] with
                    {
                        CacheControl = new Dictionary<string, string>
                        {
                            ["type"] = "ephemeral",
                        },
                    });
                attached = true;
                continue;
            }
            annotated.Insert(0, messages[i]);
        }
        if (attached)
        {
            body["messages"] = annotated;
        }
    }

    private static void EnsureConfigured(OpenAiConfig config)
    {
        if (!config.Configured || string.IsNullOrWhiteSpace(config.ApiKey))
        {
            throw new ValidationException(
                "AI provider is not configured. Set credentials in Settings (or OPENAI_API_KEY as a fallback).");
        }
    }

    private static string ReadProviderError(
        string body,
        System.Net.HttpStatusCode statusCode)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            string? message = document.RootElement
                .GetProperty("error")
                .GetProperty("message")
                .GetString();
            if (!string.IsNullOrWhiteSpace(message))
            {
                return message;
            }
        }
        catch (JsonException)
        {
            // Use the raw response when the provider did not return JSON.
        }
        catch (KeyNotFoundException)
        {
            // Use the raw response when the provider uses another error shape.
        }

        return string.IsNullOrWhiteSpace(body)
            ? $"OpenAI-compatible request failed with HTTP {(int)statusCode}."
            : body.Trim();
    }

    private static string? ReadStringProperty(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string? ReadThinking(JsonElement message)
    {
        var parts = new List<string>();
        string? reasoning = ReadStringProperty(message, "reasoning_content");
        if (!string.IsNullOrWhiteSpace(reasoning))
        {
            parts.Add(reasoning.Trim());
        }

        if (message.TryGetProperty("reasoning_details", out JsonElement details)
            && details.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in details.EnumerateArray())
            {
                string? value;
                if (item.ValueKind == JsonValueKind.String)
                {
                    value = item.GetString();
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    value = ReadStringProperty(item, "text");
                }
                else
                {
                    value = null;
                }

                if (!string.IsNullOrWhiteSpace(value))
                {
                    parts.Add(value.Trim());
                }
            }
        }

        return parts.Count == 0 ? null : string.Join("\n\n", parts);
    }

    private static OpenAiCompletionResult SplitContentAndThinking(
        string rawContent,
        string? apiThinking)
    {
        var thoughts = new List<string>();
        if (!string.IsNullOrWhiteSpace(apiThinking))
        {
            thoughts.Add(apiThinking.Trim());
        }

        string content = rawContent
            .Replace("<think>", "\u0001", StringComparison.OrdinalIgnoreCase)
            .Replace("</think>", "\u0002", StringComparison.OrdinalIgnoreCase)
            .Replace("<thinking>", "\u0001", StringComparison.OrdinalIgnoreCase)
            .Replace("</thinking>", "\u0002", StringComparison.OrdinalIgnoreCase);
        while (content.Contains('\u0001', StringComparison.Ordinal))
        {
            int start = content.IndexOf('\u0001', StringComparison.Ordinal);
            int end = content.IndexOf('\u0002', start + 1);
            if (end < 0)
            {
                break;
            }

            string thought = content[(start + 1)..end].Trim();
            if (thought.Length > 0)
            {
                thoughts.Add(thought);
            }
            content = content.Remove(start, end - start + 1);
        }

        string? thinking = thoughts.Count == 0 ? null : string.Join("\n\n", thoughts);
        return string.IsNullOrWhiteSpace(content)
            ? throw new ValidationException(
                "OpenAI-compatible provider returned an empty assistant message.")
            : new OpenAiCompletionResult(content.Trim(), thinking);
    }
}
