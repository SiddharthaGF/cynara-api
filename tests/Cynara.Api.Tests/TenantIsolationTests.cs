using System.Net;
using System.Text.Json;

using Cynara.Api.Tests.Support;
using Cynara.Domain.Audit;
using Cynara.Domain.Forms;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Api.Tests;

/// <summary>
/// CYN-34 cross-tenant read isolation tests. Verifies that one hospital
/// cannot read or enumerate another hospital's resources through the
/// JSON:API surface by direct ID access or collection queries.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class TenantIsolationTests : IDisposable
{
    private const string PrimaryHospitalCode = "primary";
    private const string OtherHospitalCode = "secondary";

    public TenantIsolationTests(PostgreSqlDatabaseFixture database)
    {
        Factory = new CynaraTenantWebApplicationFactory(database.Settings);
        Client = Factory.CreateAuthenticatedClientAsync(
            hospitalCode: PrimaryHospitalCode).GetAwaiter().GetResult();
        OtherClient = Factory.CreateAuthenticatedClientAsync(
            hospitalCode: OtherHospitalCode).GetAwaiter().GetResult();

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
    public async Task CrossTenant_FormDefinition_IsNotFound()
    {
        var primaryApi = new JsonApiClient(Client);
        using JsonDocument created = await primaryApi.PostResourceAsync(
            "formDefinitions",
            new
            {
                code = "isolation-form",
                name = "Isolation form",
                initialClinicalSchemaJson =
                    JsonApiWorkflow.MinimalClinicalSchema(
                        "patient-name", "patient.name"),
            }).ConfigureAwait(false);
        string definitionId = JsonApiClient.RequireId(created);

        using HttpResponseMessage primary = await Client
            .GetAsync(
                new Uri(
                    $"/api/formDefinitions/{definitionId}",
                    UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, primary.StatusCode);

        using HttpClient secondaryClient = await Factory
            .CreateAuthenticatedClientAsync(hospitalCode: OtherHospitalCode)
            .ConfigureAwait(false);
        using HttpResponseMessage other = await secondaryClient
            .GetAsync(
                new Uri(
                    $"/api/formDefinitions/{definitionId}",
                    UriKind.Relative))
            .ConfigureAwait(false);
        string body = await other.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.True(
            other.StatusCode == HttpStatusCode.NotFound,
            $"Expected 404, got {other.StatusCode}. Body: {body}");
    }

    [Fact]
    public async Task SameCode_DifferentHospitals_BothExist()
    {
        await using CynaraTenantWebApplicationFactory.FactoryScope scope =
            Scope;
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
        await using CynaraTenantWebApplicationFactory.FactoryScope scope =
            Scope;
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

    [Fact]
    public async Task CrossTenant_FormVersion_IsNotFound()
    {
        var primaryApi = new JsonApiClient(Client);
        using JsonDocument created = await primaryApi.PostResourceAsync(
            "formDefinitions",
            new
            {
                code = "cross-tenant-version-read",
                name = "Cross-tenant version read",
                initialClinicalSchemaJson =
                    JsonApiWorkflow.MinimalClinicalSchema(
                        "field", "field.code"),
            }).ConfigureAwait(false);
        string definitionId = JsonApiClient.RequireId(created);

        using JsonDocument definition = await primaryApi
            .GetAsync(
                $"/api/formDefinitions/{definitionId}?include=versions")
            .ConfigureAwait(false);
        string versionId = definition.RootElement.GetProperty("included")
            .EnumerateArray()
            .First()
            .GetProperty("id")
            .GetString()!;

        using HttpClient secondaryClient = await Factory
            .CreateAuthenticatedClientAsync(hospitalCode: OtherHospitalCode)
            .ConfigureAwait(false);
        using HttpResponseMessage response = await secondaryClient
            .GetAsync(
                new Uri(
                    $"/api/formVersions/{versionId}",
                    UriKind.Relative))
            .ConfigureAwait(false);
        Assert.True(
            response.StatusCode == HttpStatusCode.NotFound,
            "Expected 404 for cross-tenant form version read, "
            + $"got {response.StatusCode}.");
    }

    [Fact]
    public async Task
        CrossTenant_FormDefinition_Collection_HidesOtherTenant()
    {
        var primaryApi = new JsonApiClient(Client);
        using JsonDocument created = await primaryApi.PostResourceAsync(
            "formDefinitions",
            new
            {
                code = "hidden-form",
                name = "Should be hidden from other tenant",
                initialClinicalSchemaJson =
                    JsonApiWorkflow.MinimalClinicalSchema(
                        "field", "field.code"),
            }).ConfigureAwait(false);
        string definitionId = JsonApiClient.RequireId(created);

        using HttpResponseMessage primary = await Client
            .GetAsync(new Uri("/api/formDefinitions", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, primary.StatusCode);
        string primaryBody = await primary.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains(
            definitionId, primaryBody, StringComparison.Ordinal);

        using HttpClient secondaryClient = await Factory
            .CreateAuthenticatedClientAsync(hospitalCode: OtherHospitalCode)
            .ConfigureAwait(false);
        using HttpResponseMessage secondary = await secondaryClient
            .GetAsync(new Uri("/api/formDefinitions", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, secondary.StatusCode);
        string secondaryBody = await secondary.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.DoesNotContain(
            definitionId, secondaryBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        CrossTenant_AuditEvent_Collection_HidesOtherTenant()
    {
        await using CynaraTenantWebApplicationFactory.FactoryScope scope =
            Scope;
        CynaraDbContext dbContext = scope.DbContext;
        Guid primaryId = scope.LoadPrimaryHospital().Id;
        Guid otherId = scope.LoadOtherHospital().Id;

        AuditEvent primaryEvent = new()
        {
            Id = Guid.NewGuid(),
            HospitalId = primaryId,
            ResourceType = "form-version",
            ResourceId = Guid.NewGuid(),
            Action = "create",
            ActorId = "primary-auditor",
            OccurredAt = DateTimeOffset.UtcNow,
        };
        AuditEvent otherEvent = new()
        {
            Id = Guid.NewGuid(),
            HospitalId = otherId,
            ResourceType = "form-version",
            ResourceId = Guid.NewGuid(),
            Action = "create",
            ActorId = "other-auditor",
            OccurredAt = DateTimeOffset.UtcNow,
        };
        dbContext.AuditEvents.AddRange(primaryEvent, otherEvent);
        _ = await dbContext.SaveChangesAsync().ConfigureAwait(false);

        using HttpResponseMessage primaryResponse = await Client
            .GetAsync(new Uri("/api/auditEvents", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, primaryResponse.StatusCode);
        string primaryBody = await primaryResponse.Content
            .ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains(
            primaryEvent.Id.ToString(),
            primaryBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            otherEvent.Id.ToString(),
            primaryBody,
            StringComparison.Ordinal);

        using HttpResponseMessage secondaryResponse = await OtherClient
            .GetAsync(new Uri("/api/auditEvents", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, secondaryResponse.StatusCode);
        string secondaryBody = await secondaryResponse.Content
            .ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains(
            otherEvent.Id.ToString(),
            secondaryBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            primaryEvent.Id.ToString(),
            secondaryBody,
            StringComparison.Ordinal);
    }
}
