using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Cynara.Domain.Invitations;

namespace Cynara.Api.Tests.Invitations;

/// <summary>
/// Admin invitation lifecycle over the real-auth HTTP surface: creation
/// lands pending with exactly one atomic audit event; cancellation kills
/// the link immediately and illegal re-transitions write no audit; lazy
/// expiry on listing notifies hospital-scoped user-invitations.read
/// holders; resend supersedes the hash and restarts 72-hour validity;
/// actors without the capability grant receive 403 everywhere.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed partial class InvitationsAdminLifecycleTests : IDisposable
{
    private const string HospitalCode = "inv-admin";
    private const string Password = "Cynara!Dev123";
    private const string ActorAdmin = "actor-admin";

    public InvitationsAdminLifecycleTests(PostgreSqlDatabaseFixture database)
    {
        Database = database.Settings;
        Factory = new RecordingInvitationFactory(Database);
    }

    public void Dispose()
    {
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    private RecordingInvitationFactory Factory { get; }

    private TestDatabaseSettings Database { get; }

    [Fact]
    public async Task Create_ThenCancel_FollowsLifecycle_AuditsAtomically()
    {
        HttpClient client = await SeedAdminAsync().ConfigureAwait(false);

        using HttpResponseMessage created = await client
            .PostAsJsonAsync(
                "/api/user-invitations",
                new { email = "invitee@cynara.dev" })
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        (Guid id, string token) = await ReadCreatedAsync(created)
            .ConfigureAwait(false);

        Assert.Equal(1, await CountAuditsAsync("invitation.created")
            .ConfigureAwait(false));
        Invitation row = await LoadRowAsync(id).ConfigureAwait(false);
        Assert.Equal(InvitationStatus.Pending, row.Status);
        Assert.NotEqual(token, row.TokenHash);

        using HttpResponseMessage cancelled = await client
            .PostAsync(
                $"/api/user-invitations/{id}/cancel",
                content: null)
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal(
            InvitationStatus.Cancelled,
            (await LoadRowAsync(id).ConfigureAwait(false)).Status);

        using HttpResponseMessage again = await client
            .PostAsync(
                $"/api/user-invitations/{id}/cancel",
                content: null)
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(1, await CountAuditsAsync("invitation.created")
            .ConfigureAwait(false));
        Assert.Equal(1, await CountAuditsAsync("invitation.cancelled")
            .ConfigureAwait(false));

        using HttpResponseMessage list = await client
            .GetAsync("/api/user-invitations").ConfigureAwait(false);
        string listBody = await list.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.DoesNotContain(token, listBody, StringComparison.Ordinal);
        Assert.DoesNotContain("token", listBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LazyExpiry_NotifiesHolders_Resend_RestartsWindow()
    {
        HttpClient client = await SeedAdminAsync().ConfigureAwait(false);
        using HttpResponseMessage created = await client
            .PostAsJsonAsync(
                "/api/user-invitations",
                new { email = "late@cynara.dev" })
            .ConfigureAwait(false);
        (Guid id, string _) = await ReadCreatedAsync(created)
            .ConfigureAwait(false);
        string originalHash = (await LoadRowAsync(id).ConfigureAwait(false))
            .TokenHash;

        await BackdateValidityAsync(id).ConfigureAwait(false);
        Factory.ResetNotifications();
        using HttpResponseMessage list = await client
            .GetAsync("/api/user-invitations").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using JsonDocument document = await ReadJsonAsync(list)
            .ConfigureAwait(false);
        JsonElement item =
            Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(id, item.GetProperty("id").GetGuid());
        Assert.Equal("Expired", item.GetProperty("status").GetString());
        Assert.Equal(1, await CountAuditsAsync("invitation.expired")
            .ConfigureAwait(false));
        RecordedExpiryNotice notice =
            Assert.Single(Factory.Notifier.Calls);
        Assert.Equal(id, notice.Notice.InvitationId);
        Assert.Equal([ActorAdmin], notice.Recipients);

        DateTimeOffset beforeResend = DateTimeOffset.UtcNow;
        using HttpResponseMessage resent = await client
            .PostAsync(
                $"/api/user-invitations/{id}/resend",
                content: null)
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, resent.StatusCode);
        using JsonDocument resentDoc = await ReadJsonAsync(resent)
            .ConfigureAwait(false);
        JsonElement view = resentDoc.RootElement.GetProperty("invitation");
        Assert.Equal("Pending", view.GetProperty("status").GetString());
        Assert.Equal(2, view.GetProperty("linkVersion").GetInt32());
        var issuedAt = DateTimeOffset.Parse(
            view.GetProperty("issuedAt").GetString()!,
            CultureInfo.InvariantCulture);
        var expiresAt = DateTimeOffset.Parse(
            view.GetProperty("expiresAt").GetString()!,
            CultureInfo.InvariantCulture);
        Assert.True(issuedAt >= beforeResend.ToUniversalTime());
        Assert.Equal(TimeSpan.FromHours(72), expiresAt - issuedAt);
        Assert.Equal(1, await CountAuditsAsync("invitation.resent")
            .ConfigureAwait(false));

        Invitation after = await LoadRowAsync(id).ConfigureAwait(false);
        Assert.NotEqual(originalHash, after.TokenHash);
    }

    [Fact]
    public async Task Routes_RejectActorsWithoutCapabilityGrant()
    {
        HttpClient client =
            await SeedUnprivilegedAsync().ConfigureAwait(false);

        using HttpResponseMessage create = await client
            .PostAsJsonAsync(
                "/api/user-invitations",
                new { email = "denied@cynara.dev" })
            .ConfigureAwait(false);
        using HttpResponseMessage list = await client
            .GetAsync("/api/user-invitations").ConfigureAwait(false);
        var anyId = Guid.NewGuid();
        using HttpResponseMessage cancel = await client
            .PostAsync(
                $"/api/user-invitations/{anyId}/cancel",
                content: null)
            .ConfigureAwait(false);
        using HttpResponseMessage resend = await client
            .PostAsync(
                $"/api/user-invitations/{anyId}/resend",
                content: null)
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, cancel.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, resend.StatusCode);
        Assert.Equal(0, await CountRowsAsync().ConfigureAwait(false));
        Assert.Empty(Factory.Notifier.Calls);
    }
}
