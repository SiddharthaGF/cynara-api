using Cynara.Domain.Components;

namespace Cynara.Application.Modules.Components;

internal static class ComponentVersionLifecycle
{
    public enum Trigger
    {
        Publish = 0,
        Retire = 1,
    }

    public static void Fire(ComponentVersion version, Trigger trigger)
    {
        ArgumentNullException.ThrowIfNull(version);
        bool valid = (version.Status, trigger) switch
        {
            (ComponentVersionStatus.Draft, Trigger.Publish) => true,
            (ComponentVersionStatus.Published, Trigger.Retire) => true,
            _ => false,
        };
        if (!valid)
        {
            throw new InvalidStateException(
                $"Cannot {FormatTrigger(trigger)} a component version in status '{version.Status}'.");
        }

        version.Status = trigger switch
        {
            Trigger.Publish => ComponentVersionStatus.Published,
            Trigger.Retire => ComponentVersionStatus.Retired,
            _ => version.Status,
        };
    }

    private static string FormatTrigger(Trigger trigger)
    {
        return trigger switch
        {
            Trigger.Publish => "publish",
            Trigger.Retire => "retire",
            _ => trigger.ToString(),
        };
    }
}
