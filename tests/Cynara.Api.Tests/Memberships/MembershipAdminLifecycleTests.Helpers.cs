using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Cynara.Domain.Capabilities;
using Cynara.Domain.Memberships;
using Cynara.Infrastructure.Modules.Identity;

namespace Cynara.Api.Tests.Memberships;

/// <summary>
/// Seeding, database inspection, and HTTP helpers for
/// <see cref="MembershipAdminLifecycleTests"/>.
/// </summary>
public sealed partial class MembershipAdminLifecycleTests
{
    private sealed record MembershipRow(
        Guid Id,
        string Status,
        DateTimeOffset? RevokedAt);

    private Task<HttpClient> SeedAdminAsync()
    {
        return SeedCallerAsync(
            HospitalCode,
            "Hospital Memb",
            grantCapabilities: true);
    }

    private Task<HttpClient> SeedUnprivilegedAsync()
    {
        return SeedCallerAsync(
            HospitalCode,
            "Hospital Memb",
            grantCapabilities: false);
    }

    private async Task<HttpClient> SeedOtherHospitalAsync()
    {
        AuthTokenResult tokens = await SeedHospitalDataAsync(
            OtherHospitalCode,
            "Hospital Other",
            grantCapabilities: true).ConfigureAwait(false);
        return MakeClient(tokens, OtherHospitalCode);
    }

    private Task<HttpClient> SeedCallerAsync(
        string hospitalCode,
        string hospitalName,
        bool grantCapabilities)
    {
        return SeedAndConnectAsync(
            hospitalCode,
            hospitalName,
            grantCapabilities);
    }

    private async Task<HttpClient> SeedAndConnectAsync(
        string hospitalCode,
        string hospitalName,
        bool grantCapabilities)
    {
        await Factory.ResetDatabaseAsync().ConfigureAwait(false);
        AuthTokenResult tokens = await SeedHospitalDataAsync(
            hospitalCode,
            hospitalName,
            grantCapabilities).ConfigureAwait(false);
        return MakeClient(tokens, hospitalCode);
    }

    private async Task<AuthTokenResult> SeedHospitalDataAsync(
        string hospitalCode,
        string hospitalName,
        bool grantCapabilities)
    {
        await Factory.RegisterClientAsync().ConfigureAwait(false);
        Guid hospitalId = (await Factory.EnsureHospitalAsync(
            hospitalCode,
            hospitalName).ConfigureAwait(false)).Id;
        CynaraUser admin = await Factory.CreateUserAsync(
            "memb-admin@cynara.dev",
            Password).ConfigureAwait(false);
        await Factory.SeedMembershipAsync(admin, hospitalId, ActorAdmin)
            .ConfigureAwait(false);
        if (grantCapabilities)
        {
            await Factory.SeedCapabilityAsync(
                hospitalId,
                ActorAdmin,
                CapabilityCodes.MembershipsRead).ConfigureAwait(false);
            await Factory.SeedCapabilityAsync(
                hospitalId,
                ActorAdmin,
                CapabilityCodes.MembershipsWrite).ConfigureAwait(false);
        }

        return await Factory.GetPasswordTokenAsync(
            "memb-admin@cynara.dev",
            Password).ConfigureAwait(false);
    }

    private HttpClient MakeClient(
        AuthTokenResult tokens,
        string hospitalCode)
    {
        HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new("Bearer", tokens.AccessToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            hospitalCode);
        return client;
    }

    private async Task<Guid> HospitalIdAsync(string hospitalCode)
    {
        await using AsyncServiceScope scope =
            Factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        return await dbContext.Hospitals
            .Where(item => item.Code == hospitalCode)
            .Select(item => item.Id)
            .SingleAsync().ConfigureAwait(false);
    }

    private static async Task<Guid> AddAsync(
        HttpClient client,
        Guid userId,
        string actorId)
    {
        using HttpResponseMessage response = await client
            .PostAsJsonAsync(
                "/api/memberships",
                new { userId, actorId })
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement view = await ReadViewAsync(response)
            .ConfigureAwait(false);
        return view.GetProperty("id").GetGuid();
    }

    private async Task<MembershipRow> LoadMembershipAsync(Guid id)
    {
        await using AsyncServiceScope scope =
            Factory.Services.CreateAsyncScope();
        CynaraIdentityDbContext identity = scope.ServiceProvider
            .GetRequiredService<CynaraIdentityDbContext>();
        return await identity.Memberships
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new MembershipRow(
                item.Id,
                item.Status.ToString(),
                item.RevokedAt))
            .SingleAsync().ConfigureAwait(false);
    }

    private async Task<int> CountAuditsAsync(string action)
    {
        await using AsyncServiceScope scope =
            Factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        return await dbContext.AuditEvents
            .Where(item => item.ResourceType == "membership"
                && item.Action == action)
            .CountAsync().ConfigureAwait(false);
    }

    private async Task<int> CountMembershipsAsync(Guid hospitalId)
    {
        await using AsyncServiceScope scope =
            Factory.Services.CreateAsyncScope();
        CynaraIdentityDbContext identity = scope.ServiceProvider
            .GetRequiredService<CynaraIdentityDbContext>();
        return await identity.Memberships
            .Where(item => item.HospitalId == hospitalId)
            .CountAsync().ConfigureAwait(false);
    }

    private async Task<int> CountActiveForUserAsync(Guid userId)
    {
        await using AsyncServiceScope scope =
            Factory.Services.CreateAsyncScope();
        CynaraIdentityDbContext identity = scope.ServiceProvider
            .GetRequiredService<CynaraIdentityDbContext>();
        return await identity.Memberships
            .Where(item => item.UserId == userId
                && item.Status == MembershipStatus.Active)
            .CountAsync().ConfigureAwait(false);
    }

    private static async Task<JsonElement> ReadViewAsync(
        HttpResponseMessage response)
    {
        using JsonDocument document = await ReadJsonAsync(response)
            .ConfigureAwait(false);
        return document.RootElement.Clone();
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        return JsonDocument.Parse(body);
    }
}
