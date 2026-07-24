using System.Globalization;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Cynara.Api.Tests.Support;

internal static class JsonApiMedia
{
    public const string ContentType = "application/vnd.api+json";
}

internal sealed class JsonApiClient(HttpClient httpClient)
{
    public HttpClient Http { get; } = httpClient;

    public void UseHospitalContext(string? hospitalCode)
    {
        if (string.IsNullOrWhiteSpace(hospitalCode))
        {
            Http.DefaultRequestHeaders.Remove("X-Hospital-Code");
            return;
        }

        if (Http.DefaultRequestHeaders.Contains("X-Hospital-Code"))
        {
            _ = Http.DefaultRequestHeaders.Remove("X-Hospital-Code");
        }

        _ = Http.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            hospitalCode);
    }

    public async Task<JsonDocument> PostResourceAsync(
        string resourceType,
        object attributes,
        object? relationships = null,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = resourceType,
            ["attributes"] = attributes,
        };
        if (relationships is not null)
        {
            data["relationships"] = relationships;
        }

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["data"] = data,
        };

        using StringContent content = CreateJsonApiContent(payload);
        using HttpResponseMessage response = await Http
            .PostAsync(
                new Uri($"/api/{resourceType}", UriKind.Relative),
                content,
                cancellationToken)
            .ConfigureAwait(false);
        return await ReadDocumentAsync(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<JsonDocument> PatchResourceAsync(
        string resourceType,
        string id,
        object attributes,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            data = new
            {
                type = resourceType,
                id,
                attributes,
            },
        };
        using StringContent content = CreateJsonApiContent(payload);
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            new Uri($"/api/{resourceType}/{id}", UriKind.Relative))
        {
            Content = content,
        };
        using HttpResponseMessage response = await Http
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        return await ReadDocumentAsync(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<JsonDocument> GetAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await Http
            .GetAsync(new Uri(path, UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);
        return await ReadDocumentAsync(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> SendGetAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        return await Http
            .GetAsync(new Uri(path, UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> DeleteAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        return await Http
            .DeleteAsync(new Uri(path, UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<JsonDocument> PostActionAsync(
        string path,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await PostActionRawAsync(
            path,
            body,
            cancellationToken).ConfigureAwait(false);
        return await ReadDocumentAsync(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> PostActionRawAsync(
        string path,
        object? body,
        CancellationToken cancellationToken = default)
    {
        string resolvedPath = AppendQueryFromObject(path, body);
        return await Http
            .PostAsync(
                new Uri(resolvedPath, UriKind.Relative),
                content: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public static string AppendQueryFromObject(string path, object? body)
    {
        if (body is null)
        {
            return path;
        }

        var pairs = new List<string>();
        foreach (PropertyInfo property in body.GetType().GetProperties(
            BindingFlags.Instance | BindingFlags.Public))
        {
            object? value = property.GetValue(body);
            if (value is null)
            {
                continue;
            }

            string encoded = Uri.EscapeDataString(
                Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
            pairs.Add($"{property.Name}={encoded}");
        }

        if (pairs.Count == 0)
        {
            return path;
        }

        string separator = path.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return path + separator + string.Join('&', pairs);
    }

    public static StringContent CreateJsonApiContent(object payload)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue(
            JsonApiMedia.ContentType);
        return content;
    }

    public static async Task<JsonDocument> ReadDocumentAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        string text = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"HTTP {(int)response.StatusCode}: {text}"));
        }

        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    public static string RequireId(JsonDocument document)
    {
        return document.RootElement.GetProperty("data").GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Missing data.id");
    }

    public static string? AttrString(JsonDocument document, string name)
    {
        JsonElement attributes = document.RootElement
            .GetProperty("data")
            .GetProperty("attributes");
        return attributes.TryGetProperty(name, out JsonElement value)
            ? value.GetString()
            : null;
    }

    public static uint AttrUInt(JsonDocument document, string name)
    {
        return document.RootElement
            .GetProperty("data")
            .GetProperty("attributes")
            .GetProperty(name)
            .GetUInt32();
    }
}

internal static class JsonApiHttpExtensions
{
    public static void AcceptJsonApi(this HttpClient client)
    {
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(JsonApiMedia.ContentType));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }
}
