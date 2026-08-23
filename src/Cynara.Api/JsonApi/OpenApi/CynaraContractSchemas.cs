using System.Text.Json.Serialization;

namespace Cynara.Api.JsonApi.OpenApi;

/// <summary>
/// Reusable JSON:API error document envelope mirroring the wire shape emitted
/// by <c>CynaraErrorMapping</c> for both transports: a top-level
/// <c>errors</c> array with string <c>status</c>, optional <c>code</c>,
/// <c>title</c>, <c>detail</c>, and optional <c>source.pointer</c>.
/// </summary>
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
