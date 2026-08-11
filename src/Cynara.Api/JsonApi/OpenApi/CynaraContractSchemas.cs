using System.Text.Json.Serialization;

namespace Cynara.Api.JsonApi.OpenApi;

/// <summary>
/// Reusable JSON:API error document envelope. Mirrors the wire shape emitted
/// by the shared <c>CynaraErrorMapping</c> for both the JSON:API pipeline and
/// the minimal-API error handler: a top-level <c>errors</c> array whose items
/// carry <c>status</c> as a string, an optional machine <c>code</c>, a human
/// <c>title</c>, a <c>detail</c>, and an optional <c>source.pointer</c> for
/// field-level validation failures.
/// </summary>
/// <remarks>
/// Referenced from <c>[ProducesResponseType]</c> on custom controllers and
/// injected by <see cref="JsonApiErrorResponseFilter"/> for JSON:API workflow
/// actions that return untyped <c>IActionResult</c>.
/// </remarks>
public sealed record JsonApiErrorDocument(IReadOnlyList<JsonApiError> Errors);

/// <summary>One error object inside a <see cref="JsonApiErrorDocument"/>.</summary>
/// <param name="Status">HTTP status code as a string, e.g. "400".</param>
/// <param name="Code">Machine-readable error code, when available.</param>
/// <param name="Title">Short human-readable summary of the problem.</param>
/// <param name="Detail">Human-readable explanation specific to this occurrence.</param>
/// <param name="Source">Optional pointer to the offending request field.</param>
public sealed record JsonApiError(
    string Status,
    string? Code,
    string Title,
    string Detail,
    JsonApiErrorSource? Source);

/// <summary>Source location for a JSON:API error object.</summary>
/// <param name="PointerPath">JSON pointer to the offending request field.</param>
public sealed record JsonApiErrorSource(
    [property: JsonPropertyName("pointer")] string PointerPath);
