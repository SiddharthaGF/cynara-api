using Cynara.Api.JsonApi.OpenApi;

using Microsoft.OpenApi;

namespace Cynara.Api.Tests;

/// <summary>
/// Behavioral tests for <see cref="OpenApiSecurityJsonTransform"/> and
/// <see cref="OpenApiDocumentSerializer"/> covering the degenerate-only,
/// byte-preserving security rewrite and compact deterministic serialization.
/// </summary>
public sealed class OpenApiSerializationTests
{
    /// <summary>
    /// The canonical AND-ed requirement the transform restores to dismiss the
    /// framework's dropped scheme names: a bearer token plus the hospital header.
    /// </summary>
    private const string CanonicalRequirement = /*lang=json,strict*/
        "[{\"Bearer\":[],\"HospitalCode\":[]}]";

    [Theory]
    [InlineData(/*lang=json,strict*/ "{\"security\":[{}]}")]
    [InlineData(/*lang=json,strict*/ "{\"security\":[ {},{} ]}")]
    [InlineData(/*lang=json,strict*/ "{\"security\":[]}")]
    public void Apply_RewritesDegenerateRequirementsToCanonical(string input)
    {
        string result = OpenApiSecurityJsonTransform.Apply(input);

        Assert.Contains(CanonicalRequirement, result, StringComparison.Ordinal);
        Assert.DoesNotContain("[{}]", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_LeavesAlreadyNamedRequirementUntouched()
    {
        const string json = /*lang=json,strict*/
            "{\"security\":[{\"ApiKey\":[]}]}";

        string result = OpenApiSecurityJsonTransform.Apply(json);

        Assert.Equal(json, result);
    }

    /// <summary>
    /// The tenant-exempt listing is the one authenticated route that must never
    /// advertise the hospital header, so it rewrites to bearer-only.
    /// </summary>
    [Fact]
    public void Apply_RewritesListingRouteDegenerateRequirementToBearerOnly()
    {
        const string input = /*lang=json,strict*/
            "{\"paths\":{\"/api/me/hospitals\":{\"get\":{\"security\":[{}]}}}}";

        string result = OpenApiSecurityJsonTransform.Apply(input);

        Assert.Contains(/*lang=json,strict*/ "[{\"Bearer\":[]}]", result, StringComparison.Ordinal);
        Assert.DoesNotContain("HospitalCode", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_RewritesTenantRouteDegenerateRequirementToCanonicalPair()
    {
        const string input = /*lang=json,strict*/
            "{\"paths\":{\"/api/formDefinitions\":{\"get\":{\"security\":[{}]}}}}";

        string result = OpenApiSecurityJsonTransform.Apply(input);

        Assert.Contains(CanonicalRequirement, result, StringComparison.Ordinal);
        Assert.DoesNotContain("[{}]", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_RewritesMixedPathsIndependently()
    {
        const string input = /*lang=json,strict*/
            "{\"paths\":{\"/api/me/hospitals\":{\"get\":{\"security\":[{}]}},\"/api/me/capabilities\":{\"get\":{\"security\":[{}]}}}}";

        string result = OpenApiSecurityJsonTransform.Apply(input);

        Assert.Contains(
            "\"/api/me/hospitals\":{\"get\":{\"security\":[{\"Bearer\":[]}]}}",
            result,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"/api/me/capabilities\":{\"get\":{\"security\":"
            + CanonicalRequirement + "}}",
            result,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_LeavesUnrelatedAndInStringContentUntouched()
    {
        const string json =
            "{\"a\":[{}],\"security\":null,\"b\":{\"c\":[1,2]},"
            + "\"note\":\"security: [{}]\"}";

        string result = OpenApiSecurityJsonTransform.Apply(json);

        Assert.Equal(json, result);
    }

    [Fact]
    public void Apply_IsBytePreservingBeyondTheSingleEditedSpan()
    {
        const string input = /*lang=json,strict*/
            "{\"servers\":[{\"url\":\"https://x\"}],\"paths\":{\"/a\":{\"get\":{\"security\":[{}]}}}}";
        string prefix =
            input[..input.IndexOf("\"/a\"", StringComparison.Ordinal)];
        const string suffix = "}";

        string result = OpenApiSecurityJsonTransform.Apply(input);

        Assert.StartsWith(prefix, result, StringComparison.Ordinal);
        Assert.EndsWith(suffix, result, StringComparison.Ordinal);
        Assert.Contains(CanonicalRequirement, result, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_ProducesTerseSingleLineSecurityCorrectJson()
    {
        string json = OpenApiDocumentSerializer.Serialize(BuildDocument());

        Assert.DoesNotContain('\n', json);
        Assert.Contains(
            "\"Bearer\":[],\"HospitalCode\":[]",
            json,
            StringComparison.Ordinal);
        Assert.False(
            json.Contains("X-Actor-Id", StringComparison.Ordinal),
            "The serialized document must not carry the legacy header scheme.");
    }

    [Fact]
    public void Serialize_IsDeterministicAcrossCalls()
    {
        string first = OpenApiDocumentSerializer.Serialize(BuildDocument());
        string second = OpenApiDocumentSerializer.Serialize(BuildDocument());

        Assert.Equal(first, second);
    }

    private static OpenApiDocument BuildDocument()
    {
        var requirement = new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer")] = [],
            [new OpenApiSecuritySchemeReference("HospitalCode")] = [],
        };

        var operations = new Dictionary<HttpMethod, OpenApiOperation>
        {
            [HttpMethod.Get] = new OpenApiOperation
            {
                Responses = new OpenApiResponses
                {
                    ["200"] = new OpenApiResponse { Description = "OK" },
                },
                Security = [requirement],
            },
        };

        return new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "Cynara API", Version = "v1" },
            Paths = new OpenApiPaths
            {
                ["/api/formDefinitions"] = new OpenApiPathItem
                {
                    Operations = operations,
                },
            },
        };
    }
}
