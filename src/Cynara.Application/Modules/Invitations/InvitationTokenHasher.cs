using System.Security.Cryptography;
using System.Text;

namespace Cynara.Application.Modules.Invitations;

/// <summary>
/// Hashes raw invitation link tokens for persistence. The raw token is
/// shown to the invitee exactly once and never stored: only its SHA-256 hex
/// digest lands in the database, so a database leak cannot reactivate links.
/// Output casing is canonical uppercase so hash lookups are exact.
/// </summary>
public static class InvitationTokenHasher
{
    public static string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(digest);
    }
}
