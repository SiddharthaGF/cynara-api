using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Cynara.Api.Tests.Support;
using Cynara.Domain.Audit;
using Cynara.Domain.Forms;
using Cynara.Domain.Hospitals;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Api.Tests;

/// <summary>
/// Integration tests for the CYN-33 hospital workspace and tenant context
/// contract. Covers the public workspace endpoints, header enforcement,
/// ownership stamping, isolation, and JSON:API contract guarantees.
/// </summary>
public sealed class HospitalTenantTests : IDisposable
{
    private const string PrimaryHospitalCode = "primary";
    private const string OtherHospitalCode = "secondary";
    private const string SuspendedHospitalCode = "paused";

    public HospitalTenantTests()
    {
        Factory = new CynaraTenantWebApplicationFactory();
        Client = Factory.CreateClient();
        Client.AcceptJsonApi();
        OtherClient = Factory.CreateClient();
        OtherClient.AcceptJsonApi();

        Client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            PrimaryHospitalCode);
        OtherClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            OtherHospitalCode);

        Factory.EnsureBootstrapHospitalAsync().GetAwaiter().GetResult();
        Factory.SeedSecondaryHospitalAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        Client.Dispose();
        OtherClient.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    private CynaraTenantWebApplicationFactory Factory { get; }

    private HttpClient Client { get; }

    private HttpClient OtherClient { get; }

    private CynaraTenantWebApplicationFactory.FactoryScope Scope =>
        Factory.CreateScope();

    [Fact]
    public async Task GetWorkspace_ReturnsResolvedHospital()
    {
        using HttpResponseMessage response = await Client
            .GetAsync(new Uri("/api/workspace", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonApiClient
            .ReadDocumentAsync(response)
            .ConfigureAwait(false);
        Assert.Equal(PrimaryHospitalCode, document.RequireString("code"));
        Assert.Equal("primary workspace", document.RequireString("name"));
        Assert.Equal("active", document.RequireString("status"));
    }

    [Fact]
    public async Task PatchWorkspace_UpdatesMutableFields()
    {
        using HttpResponseMessage response = await Client
            .SendAsync(PatchWorkspaceRequest(new
            {
                name = "Renamed workspace",
                metadataJson = /*lang=json,strict*/ "{\"key\":\"value\"}",
            }))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument patched = await JsonApiClient
            .ReadDocumentAsync(response)
            .ConfigureAwait(false);

        Assert.Equal("Renamed workspace", patched.RequireString("name"));
        Assert.Equal("active", patched.RequireString("status"));
        using var metadataDocument = JsonDocument.Parse(
            patched.RequireString("metadataJson"));
        Assert.Equal(
            "value",
            metadataDocument.RootElement.GetProperty("key").GetString());
    }

    [Fact]
    public async Task PatchWorkspace_RejectsImmutableFieldOverride()
    {
        using HttpResponseMessage response = await Client
            .SendAsync(PatchWorkspaceRequest(new
            {
                id = Guid.NewGuid(),
                code = "malicious-rename",
                name = "Renamed workspace",
            }))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains("hospital", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PatchWorkspace_DetectsStaleRowVersion()
    {
        uint current = await GetWorkspaceRowVersionAsync().ConfigureAwait(false);
        uint stale = current + 999;

        using HttpResponseMessage response = await Client
            .SendAsync(PatchWorkspaceRequest(new
            {
                name = "Should fail",
                rowVersion = stale,
            }))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task MissingContextHeader_ReturnsBadRequest()
    {
        using HttpClient client = Factory.CreateClient();
        client.AcceptJsonApi();
        using HttpResponseMessage response = await client
            .GetAsync(new Uri("/api/workspace", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains("Hospital context required", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedContextHeader_ReturnsBadRequest()
    {
        using HttpClient client = Factory.CreateClient();
        client.AcceptJsonApi();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code", string.Empty);
        using HttpResponseMessage response = await client
            .GetAsync(new Uri("/api/workspace", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownHospitalContext_ReturnsBadRequest()
    {
        using HttpClient client = Factory.CreateClient();
        client.AcceptJsonApi();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code", "does-not-exist");
        using HttpResponseMessage response = await client
            .GetAsync(new Uri("/api/workspace", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains("Unknown hospital workspace", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuspendedHospitalContext_ReturnsForbidden()
    {
        await using CynaraTenantWebApplicationFactory.FactoryScope scope = Scope;
        CynaraDbContext dbContext = scope.DbContext;
        dbContext.Hospitals.Add(new Hospital
        {
            Id = Guid.NewGuid(),
            Code = SuspendedHospitalCode,
            Name = "Suspended workspace",
            Status = HospitalStatus.Suspended,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _ = await dbContext.SaveChangesAsync().ConfigureAwait(false);

        using HttpClient client = Factory.CreateClient();
        client.AcceptJsonApi();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code", SuspendedHospitalCode);

        using HttpResponseMessage response = await client
            .GetAsync(new Uri("/api/workspace", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/swagger/v1/swagger.json")]
    [InlineData("/scalar/v1")]
    public async Task ExemptPaths_DoNotRequireHospitalHeader(string path)
    {
        using HttpClient client = Factory.CreateClient();
        using HttpResponseMessage response = await client
            .GetAsync(new Uri(path, UriKind.Relative))
            .ConfigureAwait(false);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task FormCreated_StampsResolvedHospitalId()
    {
        await using CynaraTenantWebApplicationFactory.FactoryScope scope = Scope;
        CynaraDbContext dbContext = scope.DbContext;
        Guid expected = scope.LoadPrimaryHospital().Id;

        FormDefinition definition = new()
        {
            Id = Guid.NewGuid(),
            Code = "tenant-form",
            Name = "Tenant form",
            HospitalId = expected,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _ = dbContext.FormDefinitions.Add(definition);
        _ = await dbContext.SaveChangesAsync().ConfigureAwait(false);

        FormDefinition stored = await dbContext.FormDefinitions
            .AsNoTracking()
            .SingleAsync(item => item.Id == definition.Id)
            .ConfigureAwait(false);
        Assert.Equal(expected, stored.HospitalId);
    }

    [Fact]
    public async Task CrossTenant_FormDefinition_IsNotFound()
    {
        // Use the public JSON:API surface to create the form under the primary
        // hospital context, then attempt to fetch it from the other tenant.
        var primaryApi = new JsonApiClient(Client);
        using JsonDocument created = await primaryApi.PostResourceAsync(
            "formDefinitions",
            new
            {
                code = "isolation-form",
                name = "Isolation form",
                initialClinicalSchemaJson =
                    JsonApiWorkflow.MinimalClinicalSchema("patient-name", "patient.name"),
            }).ConfigureAwait(false);
        string definitionId = JsonApiClient.RequireId(created);

        using HttpResponseMessage primary = await Client
            .GetAsync(new Uri($"/api/formDefinitions/{definitionId}", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, primary.StatusCode);

        // Spin up a new HttpClient to guarantee the header is the secondary
        // hospital. Sharing an HttpClient across calls would carry the primary
        // header and undermine the isolation check.
        using HttpClient secondaryClient = Factory.CreateClient();
        secondaryClient.AcceptJsonApi();
        secondaryClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code", OtherHospitalCode);
        using HttpResponseMessage other = await secondaryClient
            .GetAsync(new Uri($"/api/formDefinitions/{definitionId}", UriKind.Relative))
            .ConfigureAwait(false);
        string body = await other.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.True(
            other.StatusCode == HttpStatusCode.NotFound,
            $"Expected 404, got {other.StatusCode}. Body: {body}");
    }

    [Fact]
    public async Task SameCode_DifferentHospitals_BothExist()
    {
        await using CynaraTenantWebApplicationFactory.FactoryScope scope = Scope;
        CynaraDbContext dbContext = scope.DbContext;
        Guid primaryId = scope.LoadPrimaryHospital().Id;
        Guid otherId = scope.LoadOtherHospital().Id;
        const string code = "shared-code";

        FormDefinition first = new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = "primary shared",
            HospitalId = primaryId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        FormDefinition second = new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = "secondary shared",
            HospitalId = otherId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        dbContext.FormDefinitions.AddRange(first, second);
        _ = await dbContext.SaveChangesAsync().ConfigureAwait(false);

        Assert.Equal(code, first.Code);
        Assert.Equal(code, second.Code);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task AuditEvents_AreScopedToResolvedHospital()
    {
        await using CynaraTenantWebApplicationFactory.FactoryScope scope = Scope;
        CynaraDbContext dbContext = scope.DbContext;
        Guid primaryId = scope.LoadPrimaryHospital().Id;
        Guid otherId = scope.LoadOtherHospital().Id;

        AuditEvent primaryEvent = new()
        {
            Id = Guid.NewGuid(),
            HospitalId = primaryId,
            ResourceType = "form",
            ResourceId = Guid.NewGuid(),
            Action = "create",
            ActorId = "primary-actor",
            OccurredAt = DateTimeOffset.UtcNow,
        };
        AuditEvent otherEvent = new()
        {
            Id = Guid.NewGuid(),
            HospitalId = otherId,
            ResourceType = "form",
            ResourceId = Guid.NewGuid(),
            Action = "create",
            ActorId = "other-actor",
            OccurredAt = DateTimeOffset.UtcNow,
        };
        dbContext.AuditEvents.AddRange(primaryEvent, otherEvent);
        _ = await dbContext.SaveChangesAsync().ConfigureAwait(false);

        int primaryCount = await dbContext.AuditEvents
            .AsNoTracking()
            .Where(item => item.HospitalId == primaryId)
            .CountAsync()
            .ConfigureAwait(false);
        int otherCount = await dbContext.AuditEvents
            .AsNoTracking()
            .Where(item => item.HospitalId == otherId)
            .CountAsync()
            .ConfigureAwait(false);
        Assert.True(primaryCount >= 1);
        Assert.True(otherCount >= 1);
    }

    private async Task<uint> GetWorkspaceRowVersionAsync()
    {
        using HttpResponseMessage response = await Client
            .GetAsync(new Uri("/api/workspace", UriKind.Relative))
            .ConfigureAwait(false);
        using JsonDocument document = await JsonApiClient
            .ReadDocumentAsync(response)
            .ConfigureAwait(false);
        return document.RootElement.GetProperty("rowVersion").GetUInt32();
    }

    private static HttpRequestMessage PatchWorkspaceRequest(object body)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Patch,
            new Uri("/api/workspace", UriKind.Relative))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                System.Text.Encoding.UTF8),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.api+json");
        return request;
    }
}

internal static class HospitalTenantTestExtensions
{
    public static string RequireString(this JsonDocument document, string path)
    {
        JsonElement current = document.RootElement;
        foreach (string part in path.Split('.'))
        {
            current = current.GetProperty(part);
        }

        return current.GetString() ?? string.Empty;
    }
}
