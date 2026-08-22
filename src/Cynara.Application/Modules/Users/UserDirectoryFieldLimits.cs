namespace Cynara.Application.Modules.Users;

/// <summary>
/// Public paging bounds for the user directory listing. Values mirror the
/// patient registry so both administrative listings behave identically on
/// the wire.
/// </summary>
public static class UserDirectoryFieldLimits
{
    /// <summary>Default page size for the directory listing.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Maximum page size for the directory listing.</summary>
    public const int MaxPageSize = 100;
}
