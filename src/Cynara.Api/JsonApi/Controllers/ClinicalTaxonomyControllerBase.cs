using System.Text.Json;

using Cynara.Api.Common.ActorContext;
using Cynara.Application;

using Microsoft.AspNetCore.Mvc;

namespace Cynara.Api.JsonApi.Controllers;

/// <summary>
/// Shared scaffold for the three clinical-taxonomy controllers
/// (facilities, clinical areas, disciplines). Centralises the JSON
/// envelope, body deserialisation, and actor extraction so each leaf
/// controller only declares its route, tags, and the four action method
/// signatures. Leaf controllers inherit the helpers and the canonical
/// <see cref="ContentType"/> / <see cref="JsonOptions"/>.
/// </summary>
public abstract class ClinicalTaxonomyControllerBase(
    IHttpContextAccessor httpContextAccessor) : ControllerBase
{
    /// <summary>application/vnd.api+json media type used by every leaf.</summary>
    protected const string ContentType = "application/vnd.api+json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Deserialises the request body into <typeparamref name="T"/>.</summary>
    /// <exception cref="ValidationException">
    /// Thrown when the body cannot be parsed as JSON.
    /// </exception>
    protected async Task<T?> ReadJsonAsync<T>(CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await JsonSerializer
                .DeserializeAsync<T>(
                    Request.Body,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new ValidationException(
                $"Request body rejected: {exception.Message}");
        }
    }

    /// <summary>Returns the X-Actor-Id for the current request, if present.</summary>
    protected string? ActorId()
    {
        return httpContextAccessor.HttpContext?.GetActorId();
    }
}
