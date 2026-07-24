using System.ClientModel;

using Cynara.Application;
using Cynara.Infrastructure.Modules.FormAi;

namespace Cynara.Api.Tests;

public sealed class OpenAiProviderErrorMapperTests
{
    [Fact]
    public void Map_UnauthorizedClientResultException_DoesNotLeakApiKey()
    {
        const string leaked =
            "HTTP 401 (invalid_request_error: invalid_api_key) " +
            "Incorrect API key provided: sk-test-secret-leaked-key. " +
            "You can find your API key at " +
            "https://platform.openai.com/account/api-keys.";
        var providerError = new ClientResultException(leaked);

        // Status setter is protected; production SDK sets it from the HTTP response.
        typeof(ClientResultException)
            .GetProperty(nameof(ClientResultException.Status))!
            .SetValue(providerError, 401);

        ValidationException mapped = OpenAiProviderErrorMapper.Map(providerError);

        Assert.DoesNotContain("sk-test", mapped.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "secret",
            mapped.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "invalid_api_key",
            mapped.Message,
            StringComparison.Ordinal);
        Assert.Equal(
            "AI provider authentication failed. Check the API key in Settings.",
            mapped.Message);
        Assert.Same(providerError, mapped.InnerException);
    }

    [Fact]
    public void FromStatus_Unauthorized_ReturnsAuthGuidanceWithoutProviderText()
    {
        string message = OpenAiProviderErrorMapper.FromStatus(401);

        Assert.Equal(
            "AI provider authentication failed. Check the API key in Settings.",
            message);
        Assert.DoesNotContain("sk-", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        403,
        "AI provider authentication failed. Check the API key in Settings.")]
    [InlineData(
        404,
        "AI provider endpoint was not found. Check the base URL in Settings.")]
    [InlineData(
        429,
        "AI provider is rate-limiting or timed out. Try again shortly.")]
    [InlineData(
        503,
        "AI provider is temporarily unavailable. Try again shortly.")]
    [InlineData(
        400,
        "OpenAI-compatible request failed with HTTP 400.")]
    public void FromStatus_MapsKnownCodesToSafeMessages(
        int status,
        string expected)
    {
        Assert.Equal(expected, OpenAiProviderErrorMapper.FromStatus(status));
    }

    [Fact]
    public void Map_HttpRequestException_DoesNotForwardRawMessage()
    {
        var networkError = new HttpRequestException(
            "Connection refused while calling " +
            "https://evil.example/v1?key=sk-live-abc");

        ValidationException mapped = OpenAiProviderErrorMapper.Map(networkError);

        Assert.DoesNotContain("sk-live", mapped.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "evil.example",
            mapped.Message,
            StringComparison.Ordinal);
        Assert.Equal(
            "Could not reach the AI provider. Check the base URL and network connectivity.",
            mapped.Message);
    }
}
