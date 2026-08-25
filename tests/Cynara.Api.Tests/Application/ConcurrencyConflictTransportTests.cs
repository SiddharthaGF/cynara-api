// Application-layer contracts live in this folder, but the namespace stays
// flat as Cynara.Api.Tests: a child namespace would shadow the walking
// resolution existing tests rely on for Cynara.Application.* references.
#pragma warning disable IDE0130 // Namespace does not match folder structure
using System.Net;
using System.Text.Json;

using Cynara.Api.Common.ErrorHandling;

using Microsoft.AspNetCore.Http;

namespace Cynara.Api.Tests;

/// <summary>
/// Focused transport-level proof that a raw EF optimistic-concurrency
/// conflict renders as the canonical 409 "Concurrency conflict" document in
/// the minimal-API envelope. The JSON:API projection of the same shared
/// document is proven end-to-end by WorkflowConcurrencyConflictTests.
/// </summary>
public sealed class ConcurrencyConflictTransportTests
{
    [Fact]
    public async Task MinimalApiEnvelope_RendersEfConcurrencyConflictAs409()
    {
        IResult result = ProblemDetailsMapping.FromException(
            new DbUpdateConcurrencyException(
                "The database operation was expected to affect 1 row(s), "
                + "but actually affected 0 row(s)."));

        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddLogging()
                .BuildServiceProvider(),
        };
        await using MemoryStream body = new();
        context.Response.Body = body;

        await result.ExecuteAsync(context).ConfigureAwait(false);

        Assert.Equal(
            (int)HttpStatusCode.Conflict,
            context.Response.StatusCode);
        Assert.StartsWith(
            "application/vnd.api+json",
            context.Response.ContentType,
            StringComparison.Ordinal);
        body.Position = 0;
        using JsonDocument document = await JsonDocument.ParseAsync(body)
            .ConfigureAwait(false);
        JsonElement error = document.RootElement.GetProperty("errors")[0];
        Assert.Equal("409", error.GetProperty("status").GetString());
        Assert.Equal(
            "Concurrency conflict",
            error.GetProperty("title").GetString());
        Assert.Equal(
            "The resource was modified by another request.",
            error.GetProperty("detail").GetString());
    }
}
