using System.ClientModel;
using System.ClientModel.Primitives;

using Cynara.Application.Modules.FormAi;

using Microsoft.Extensions.AI;

using OpenAI;

namespace Cynara.Infrastructure.Modules.FormAi;

public static class OpenAiChatClientFactory
{
    public static IChatClient Create(OpenAiConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            throw new ArgumentException(
                "API key is required.",
                nameof(config));
        }

        var options = new OpenAIClientOptions
        {
            Endpoint = CreateEndpoint(config.BaseUrl),
            NetworkTimeout = config.NetworkTimeout > TimeSpan.Zero
                ? config.NetworkTimeout
                : TimeSpan.FromMinutes(10),
        };

        if (config.BaseUrl.Contains("openrouter", StringComparison.OrdinalIgnoreCase))
        {
            options.AddPolicy(
                new OpenAiOpenRouterHeadersPolicy(),
                PipelinePosition.PerCall);
        }

        OpenAIClient client = new(
            new ApiKeyCredential(config.ApiKey),
            options);
        return client.GetChatClient(config.Model).AsIChatClient();
    }

    private static Uri CreateEndpoint(string baseUrl)
    {
        char slash = Path.AltDirectorySeparatorChar;
        string trimmed = baseUrl.Trim().TrimEnd(slash);
        return new Uri(string.Concat(trimmed, slash), UriKind.Absolute);
    }
}
