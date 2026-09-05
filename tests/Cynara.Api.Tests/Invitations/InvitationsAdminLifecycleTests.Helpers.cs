using System.Text.Json;

using Cynara.Application.Modules.Invitations;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Invitations;
using Cynara.Infrastructure.Modules.Identity;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cynara.Api.Tests.Invitations;

/// <summary>
/// Seeding, database inspection, and the recording notifier factory for
/// <see cref="InvitationsAdminLifecycleTests"/>.
/// </summary>
public sealed partial class InvitationsAdminLifecycleTests
{
    private Task<HttpClient> SeedAdminAsync()
    {
        return SeedCallerAsync(grantCapabilities: true);
    }

    private Task<HttpClient> SeedUnprivilegedAsync()
    {
        return SeedCallerAsync(grantCapabilities: false);
    }

    private async Task<HttpClient> SeedCallerAsync(bool grantCapabilities)
    {
        await Factory.ResetDatabaseAsync().ConfigureAwait(false);
        await Factory.RegisterClientAsync().ConfigureAwait(false);
        Guid hospitalId = (await Factory.EnsureHospitalAsync(
            HospitalCode,
            "Hospital A").ConfigureAwait(false)).Id;
        CynaraUser admin = await Factory.CreateUserAsync(
            "admin@cynara.dev",
            Password).ConfigureAwait(false);
        await Factory.SeedMembershipAsync(admin, hospitalId, ActorAdmin)
            .ConfigureAwait(false);
        if (grantCapabilities)
        {
            await Factory.SeedCapabilityAsync(
                hospitalId,
                ActorAdmin,
                CapabilityCodes.UserInvitationsRead).ConfigureAwait(false);
            await Factory.SeedCapabilityAsync(
                hospitalId,
                ActorAdmin,
                CapabilityCodes.UserInvitationsWrite).ConfigureAwait(false);
        }

        AuthTokenResult tokens = await Factory.GetPasswordTokenAsync(
            "admin@cynara.dev",
            Password).ConfigureAwait(false);
        HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new("Bearer", tokens.AccessToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            HospitalCode);
        return client;
    }

    private static async Task<(Guid Id, string Token)> ReadCreatedAsync(
        HttpResponseMessage response)
    {
        using JsonDocument document = await ReadJsonAsync(response)
            .ConfigureAwait(false);
        JsonElement root = document.RootElement;
        return (
            root.GetProperty("invitation").GetProperty("id").GetGuid(),
            root.GetProperty("token").GetString()
                ?? throw new InvalidOperationException("token missing."));
    }

    private async Task BackdateValidityAsync(Guid id)
    {
        await using AsyncServiceScope scope =
            Factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        Invitation row = await dbContext.Invitations
            .SingleAsync(item => item.Id == id).ConfigureAwait(false);
        row.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        _ = await dbContext.SaveChangesAsync().ConfigureAwait(false);
    }

    private async Task<Invitation> LoadRowAsync(Guid id)
    {
        await using AsyncServiceScope scope =
            Factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        return await dbContext.Invitations
            .AsNoTracking()
            .SingleAsync(item => item.Id == id).ConfigureAwait(false);
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

    private async Task<int> CountRowsAsync()
    {
        await using AsyncServiceScope scope =
            Factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        return await dbContext.Invitations.CountAsync().ConfigureAwait(false);
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        return JsonDocument.Parse(body);
    }
}

/// <summary>
/// Real-auth factory that swaps the Development notification sink for a
/// recorder so tests can observe hospital-scoped recipient resolution.
/// </summary>
internal sealed class RecordingInvitationFactory(TestDatabaseSettings database)
    : IdentityAuthWebApplicationFactory(database, grantAllCapabilities: false)
{
    public RecordingNotifier Notifier { get; } = new();

    public void ResetNotifications()
    {
        Notifier.Calls.Clear();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IInvitationNotifier>(
                Notifier));
        });
    }
}

/// <summary>Captured expiry notice with its resolved recipients.</summary>
internal sealed record RecordedExpiryNotice(
    InvitationExpiryNotice Notice,
    IReadOnlyList<string> Recipients);

internal sealed class RecordingNotifier : IInvitationNotifier
{
    public List<RecordedExpiryNotice> Calls { get; } = [];

    public Task InvitationExpiredAsync(
        InvitationExpiryNotice notice,
        IReadOnlyList<string> recipientActorIds,
        CancellationToken cancellationToken)
    {
        Calls.Add(new RecordedExpiryNotice(notice, recipientActorIds));
        return Task.CompletedTask;
    }
}
