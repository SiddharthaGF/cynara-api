#pragma warning disable SCME0001 // JsonPatch is experimental in System.ClientModel.

using System.Text;

using Cynara.Application;
using Cynara.Application.Modules.FormAi;

using Microsoft.Extensions.AI;

using OpenAI.Chat;

using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using ChatRole = Microsoft.Extensions.AI.ChatRole;
using MeaiResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat;

namespace Cynara.Infrastructure.Modules.FormAi;

public sealed partial class OpenAiClient
{
    private enum PromptCacheMode
    {
        None = 0,
        AnthropicEphemeral = 1,
        OpenAiPromptKey = 2,
    }

    private static void EnsureConfigured(OpenAiConfig config)
    {
        if (!config.Configured || string.IsNullOrWhiteSpace(config.ApiKey))
        {
            throw new ValidationException(
                "AI provider is not configured. Set credentials in Settings (or OPENAI_API_KEY as a fallback).");
        }
    }

    private static ChatOptions CreateChatOptions(
        OpenAiConfig config,
        string? cacheScope)
    {
        string? promptCacheKey = null;
        if (!string.IsNullOrWhiteSpace(cacheScope)
            && DetectPromptCacheMode(config.BaseUrl) == PromptCacheMode.OpenAiPromptKey)
        {
            promptCacheKey = BuildPromptCacheKey(cacheScope);
        }

        return new ChatOptions
        {
            Temperature = 1f,
            ResponseFormat = config.JsonObject
                ? MeaiResponseFormat.Json
                : MeaiResponseFormat.Text,
            RawRepresentationFactory = _ =>
            {
                ChatCompletionOptions options = new()
                {
                    Temperature = 1f,
                };
                options.Patch.Set(jsonPath: "$.reasoning_split"u8, value: true);
                if (promptCacheKey is not null)
                {
                    options.Patch.Set(
                        jsonPath: "$.prompt_cache_key"u8,
                        value: promptCacheKey);
                }

                return options;
            },
        };
    }

    private static List<ChatMessage> ToChatMessages(
        IReadOnlyList<OpenAiMessage> messages,
        OpenAiConfig config,
        string? cacheScope)
    {
        bool attachAnthropicCache = !string.IsNullOrWhiteSpace(cacheScope)
            && DetectPromptCacheMode(config.BaseUrl)
                == PromptCacheMode.AnthropicEphemeral;
        int lastSystemIndex = -1;
        if (attachAnthropicCache)
        {
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (string.Equals(
                    messages[i].Role,
                    "system",
                    StringComparison.OrdinalIgnoreCase))
                {
                    lastSystemIndex = i;
                    break;
                }
            }
        }

        var result = new List<ChatMessage>(messages.Count);
        for (int i = 0; i < messages.Count; i++)
        {
            result.Add(ToChatMessage(
                messages[i],
                attachCacheControl: i == lastSystemIndex));
        }

        return result;
    }

    private static ChatMessage ToChatMessage(
        OpenAiMessage message,
        bool attachCacheControl)
    {
        ChatRole role = message.Role.ToUpperInvariant() switch
        {
            "SYSTEM" => ChatRole.System,
            "ASSISTANT" => ChatRole.Assistant,
            "TOOL" => ChatRole.Tool,
            _ => ChatRole.User,
        };

        if (!attachCacheControl)
        {
            return new ChatMessage(role, message.Content);
        }

        OpenAI.Chat.ChatMessage openAiMessage;
        if (role == ChatRole.System)
        {
            openAiMessage = new SystemChatMessage(message.Content);
        }
        else if (role == ChatRole.Assistant)
        {
            openAiMessage = new AssistantChatMessage(message.Content);
        }
        else
        {
            openAiMessage = new UserChatMessage(message.Content);
        }

        openAiMessage.Patch.Set(
            jsonPath: "$.cache_control"u8,
            utf8Json: BinaryData.FromObjectAsJson(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["type"] = "ephemeral",
                }));

        return new ChatMessage(role, message.Content)
        {
            RawRepresentation = openAiMessage,
        };
    }

    private static OpenAiCompletionResult ToCompletionResult(ChatResponse response)
    {
        var thoughts = new List<string>();
        foreach (AIContent content in response.Messages.SelectMany(static m => m.Contents))
        {
            if (content is TextReasoningContent reasoning
                && !string.IsNullOrWhiteSpace(reasoning.Text))
            {
                thoughts.Add(reasoning.Text.Trim());
            }
        }

        string? apiThinking = thoughts.Count == 0
            ? null
            : string.Join("\n\n", thoughts);
        return SplitContentAndThinking(response.Text ?? string.Empty, apiThinking);
    }

    private static OpenAiStreamDelta? ToStreamDelta(ChatResponseUpdate update)
    {
        string? content = string.IsNullOrEmpty(update.Text) ? null : update.Text;
        string? reasoning = null;
        foreach (AIContent part in update.Contents)
        {
            if (part is TextReasoningContent reasoningContent
                && !string.IsNullOrEmpty(reasoningContent.Text))
            {
                reasoning = (reasoning ?? string.Empty) + reasoningContent.Text;
            }
        }

        return content is null && reasoning is null
            ? null
            : new OpenAiStreamDelta(content, reasoning);
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
