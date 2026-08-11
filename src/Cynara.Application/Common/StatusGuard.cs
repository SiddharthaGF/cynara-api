namespace Cynara.Application.Common;

/// <summary>
/// Shared status invariant guards for catalog definitions.
/// </summary>
internal static class StatusGuard
{
    public static void EnsureNotRetired<TStatus>(
        TStatus status,
        TStatus retiredValue,
        string entityName,
        string code)
        where TStatus : struct, Enum
    {
        if (status.Equals(retiredValue))
        {
            throw new InvalidStateException(
                $"{entityName} '{code}' is already retired.");
        }
    }
}
