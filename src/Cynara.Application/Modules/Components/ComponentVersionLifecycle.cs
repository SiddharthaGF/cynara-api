using Cynara.Domain.Components;

using Stateless;

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
        StateMachine<ComponentVersionStatus, Trigger> machine = Create(version);
        if (!machine.CanFire(trigger))
        {
            throw new InvalidStateException(
                $"Cannot {FormatTrigger(trigger)} a component version in status '{version.Status}'.");
        }

        machine.Fire(trigger);
    }

    private static StateMachine<ComponentVersionStatus, Trigger> Create(
        ComponentVersion version)
    {
        var machine = new StateMachine<ComponentVersionStatus, Trigger>(
            () => version.Status,
            status => version.Status = status);

        _ = machine.Configure(ComponentVersionStatus.Draft)
            .Permit(Trigger.Publish, ComponentVersionStatus.Published);

        _ = machine.Configure(ComponentVersionStatus.Published)
            .Permit(Trigger.Retire, ComponentVersionStatus.Retired);

        _ = machine.Configure(ComponentVersionStatus.Retired);

        return machine;
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
