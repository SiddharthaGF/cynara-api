namespace Cynara.Application.Common;

/// <summary>
/// Shared optimistic-concurrency guard. Workflow helpers keep their thin
/// module-specific facade so call sites stay stable; this class owns the
/// single canonical comparison and message.
/// </summary>
internal static class ConcurrencyGuard
{
    public static void Ensure(uint current, uint provided, string entityName)
    {
        if (provided != current)
        {
            throw new ConcurrencyException(
                $"The {entityName} was modified by another request.");
        }
    }
}
