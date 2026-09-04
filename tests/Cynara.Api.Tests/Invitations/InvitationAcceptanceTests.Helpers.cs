using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

using Cynara.Application.Modules.Invitations;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Hospitals;
using Cynara.Domain.Invitations;
using Cynara.Infrastructure.Modules.Identity;

using Microsoft.AspNetCore.Identity;

namespace Cynara.Api.Tests.Invitations;

/// <summary>
/// Workspace seeding, invitation seeding, and cross-track database
/// inspection for <see cref="InvitationAcceptanceTests"/>.
/// </summary>
public sealed partial class InvitationAcceptanceTests
{
    private async Task<Guid> SeedWorkspaceAsync()
    {
        await Factory.ResetDatabaseAsync().ConfigureAwait(false);
        await Factory.RegisterClientAsync().ConfigureAwait(false);
        Hospital hospital = await Factory.EnsureHospitalAsync(
            HospitalCode, HospitalName).ConfigureAwait(false);
        return hospital.Id;
    }

    private async Task<HttpClient> SeedAdminClientAsync(Guid hospitalId)
    {
        IdentityUser<Guid> admin = await Factory.CreateUserAsync(
            AdminEmail, Password).ConfigureAwait(false);
        await Factory.SeedMembershipAsync(admin, hospitalId, ActorAdmin)
            .ConfigureAwait(false);
        await Factory.SeedCapabilityAsync(
            hospitalId,
            ActorAdmin,
            CapabilityCodes.UserInvitationsRead).ConfigureAwait(false);
        await Factory.SeedCapabilityAsync(
            hospitalId,
            ActorAdmin,
            CapabilityCodes.UserInvitationsWrite).ConfigureAwait(false);
        AuthTokenResult tokens = await Factory.GetPasswordTokenAsync(
            AdminEmail, Password).ConfigureAwait(false);
        HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new("Bearer", tokens.AccessToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            HospitalCode);
        return client;
    }

    private static async Task<(Guid Id, string Token)> CreateInvitationAsync(
        HttpClient admin,
        string email,
        string? snapshot)
    {
        using HttpResponseMessage created = await admin.PostAsJsonAsync(
            "/api/user-invitations",
            new { email, profileSnapshot = snapshot }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using JsonDocument document = await ReadJsonAsync(created)
            .ConfigureAwait(false);
        JsonElement root = document.RootElement;
        return (
            root.GetProperty("invitation").GetProperty("id").GetGuid(),
            root.GetProperty("token").GetString()
                ?? throw new InvalidOperationException("token missing."));
    }

    /// <summary>
    /// Inserts an invitation row directly, bypassing the admin workflow, so
    /// acceptance can be exercised against states the admin surface cannot
    /// produce (revoked, already-used, malformed snapshots, invalid emails).
    /// </summary>
    private async Task<(Guid Id, string Token)> SeedInvitationAsync(
        Guid hospitalId,
        string email,
        InvitationStatus status,
        string? snapshot = null,
        DateTimeOffset? expiresAt = null)
    {
        var id = Guid.NewGuid();
        string token = Convert.ToHexString(
            RandomNumberGenerator.GetBytes(32));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using AsyncServiceScope scope =
            Factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        dbContext.Invitations.Add(new Invitation
        {
            Id = id,
            Email = email,
            HospitalId = hospitalId,
            ProfileSnapshot = snapshot,
            TokenHash = InvitationTokenHasher.Hash(token),
            Status = status,
            LinkVersion = 1,
            IssuedAt = now.AddDays(-1),
            ExpiresAt = expiresAt ?? now.AddHours(72),
            CreatedAt = now,
        });
        _ = await dbContext.SaveChangesAsync().ConfigureAwait(false);
        return (id, token);
    }

    private static async Task<HttpResponseMessage> AcceptAsync(
        HttpClient client,
        string token,
        string password)
    {
        return await client.PostAsJsonAsync(
            $"/api/user-invitations/{token}/accept",
            new { password }).ConfigureAwait(false);
    }

    private async Task<Invitation> LoadInvitationAsync(Guid id)
    {
        await using AsyncServiceScope scope =
            Factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        return await dbContext.Invitations
            .AsNoTracking()
            .SingleAsync(item => item.Id == id).ConfigureAwait(false);
    }

    private async Task<int> CountUsersAsync(string email)
    {
        await using AsyncServiceScope scope =
            Factory.Services.CreateAsyncScope();
        CynaraIdentityDbContext identity = scope.ServiceProvider
            .GetRequiredService<CynaraIdentityDbContext>();
        return await identity.Users
            .CountAsync(item => item.Email == email).ConfigureAwait(false);
    }

    private async Task<IdentityUser<Guid>> LoadUserAsync(string email)
    {
        await using AsyncServiceScope scope =
            Factory.Services.CreateAsyncScope();
        CynaraIdentityDbContext identity = scope.ServiceProvider
            .GetRequiredService<CynaraIdentityDbContext>();
        return await identity.Users
            .SingleAsync(item => item.Email == email).ConfigureAwait(false);
    }

    private async Task<int> CountMembershipsAsync(
        Guid hospitalId,
        string actorId)
    {
        await using AsyncServiceScope scope =
            Factory.Services.CreateAsyncScope();
        CynaraIdentityDbContext identity = scope.ServiceProvider
            .GetRequiredService<CynaraIdentityDbContext>();
        return await identity.Memberships
            .CountAsync(item => item.HospitalId == hospitalId
                && item.ActorId == actorId).ConfigureAwait(false);
    }

    private async Task<int> CountGrantsAsync(
        Guid hospitalId,
        string actorId)
    {
        await using AsyncServiceScope scope =
            Factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        return await dbContext.CapabilityAssignments
            .CountAsync(item => item.HospitalId == hospitalId
                && item.ActorId == actorId).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<CapabilityAssignment>> LoadGrantsAsync(
        Guid hospitalId,
        string actorId)
    {
        await using AsyncServiceScope scope =
            Factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        return await dbContext.CapabilityAssignments
            .AsNoTracking()
            .Where(item => item.HospitalId == hospitalId
                && item.ActorId == actorId)
            .ToListAsync().ConfigureAwait(false);
    }

    private async Task<int> CountAuditsAsync(string action)
    {
        await using AsyncServiceScope scope =
            Factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        return await dbContext.AuditEvents
            .Where(item => item.ResourceType == "invitation"
                && item.Action == action)
            .CountAsync().ConfigureAwait(false);
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        return JsonDocument.Parse(body);
    }
}
