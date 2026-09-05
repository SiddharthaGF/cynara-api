using Microsoft.AspNetCore.Identity;

namespace Cynara.Infrastructure.Modules.Identity;

/// <summary>
/// Application identity user. Extends the stock Identity user with the
/// member's given and family names, captured at invitation acceptance when
/// the administrator did not predefine them. Both stay nullable so legacy
/// accounts and external logins without profile data keep working.
/// </summary>
public sealed class CynaraUser : IdentityUser<Guid>
{
    /// <summary>Given name; max 128 characters like patient names.</summary>
    public string? GivenName { get; set; }

    /// <summary>Family name; max 128 characters like patient names.</summary>
    public string? FamilyName { get; set; }
}
