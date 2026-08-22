using Cynara.Api.JsonApi.OpenApi;

using Microsoft.OpenApi;

namespace Cynara.Api.Tests;

/// <summary>
/// Behavioral tests for the OpenAPI serialization-support work unit:
/// <see cref="OpenApiSecurityJsonTransform"/> (byte-preserving, degenerate-only
/// security-requirement rewrite) and <see cref="OpenApiDocumentSerializer"/>
/// (compact, canonical, deterministic JSON). These directly cover behaviour that
/// previously relied only on the end-to-end snapshot/contract suites.
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

        // The empty requirement object(s) are replaced by the single canonical
        // AND-ed bearer + hospital requirement; no stray empty requirement remains.
        Assert.Contains(CanonicalRequirement, result, StringComparison.Ordinal);
        Assert.DoesNotContain("[{}]", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_LeavesAlreadyNamedRequirementUntouched()
    {
        const string json = /*lang=json,strict*/
            "{\"security\":[{\"ApiKey\":[]}]}";

        string result = OpenApiSecurityJsonTransform.Apply(json);

        // Arrays that already carry a quoted scheme name are not degenerate and
        // must survive byte-for-byte.
        Assert.Equal(json, result);
    }

    [Fact]
    public void Apply_RewritesListingRouteDegenerateRequirementToBearerOnly()
    {
        const string input = /*lang=json,strict*/
            "{\"paths\":{\"/api/me/hospitals\":{\"get\":{\"security\":[{}]}}}}";

        string result = OpenApiSecurityJsonTransform.Apply(input);

        // The tenant-exempt listing is the one authenticated route that must
        // never advertise the hospital header: a degenerate requirement under
        // it rewrites to the bearer-only shape, not the canonical AND-ed pair.
        Assert.Contains(/*lang=json,strict*/ "[{\"Bearer\":[]}]", result, StringComparison.Ordinal);
        Assert.DoesNotContain("HospitalCode", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_RewritesTenantRouteDegenerateRequirementToCanonicalPair()
    {
        const string input = /*lang=json,strict*/
            "{\"paths\":{\"/api/formDefinitions\":{\"get\":{\"security\":[{}]}}}}";

        string result = OpenApiSecurityJsonTransform.Apply(input);

        // Tenant-owned routes keep the canonical AND-ed bearer + hospital
        // requirement even after the writer drops the scheme names.
        Assert.Contains(CanonicalRequirement, result, StringComparison.Ordinal);
        Assert.DoesNotContain("[{}]", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_RewritesMixedPathsIndependently()
    {
        const string input = /*lang=json,strict*/
            "{\"paths\":{\"/api/me/hospitals\":{\"get\":{\"security\":[{}]}},\"/api/me/capabilities\":{\"get\":{\"security\":[{}]}}}}";

        string result = OpenApiSecurityJsonTransform.Apply(input);

        // One document, two routes: the listing becomes bearer-only while the
        // tenant-owned route keeps the canonical pair.
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

        // Only a JSON `security` key whose value is a degenerate array is
        // rewritten; arrays under other keys, null values, and the literal text
        // "security: [{}]" inside a string value are all preserved exactly.
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

        // Terse serialization is a single line (no indentation newlines).
        Assert.DoesNotContain('\n', json);

        // The security requirement is emitted with its scheme names restored in
        // the canonical AND-ed bearer + hospital shape.
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
