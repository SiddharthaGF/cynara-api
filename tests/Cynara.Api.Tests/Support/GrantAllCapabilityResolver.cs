using Cynara.Application.Modules.Capabilities;

namespace Cynara.Api.Tests.Support;

/// <summary>
/// Test double that grants every capability to the current actor. Used by
/// the default test factory so existing lifecycle/tenant suites keep working
/// without per-actor assignment seeding. Enforcement tests construct a
/// factory without this override and seed real assignments instead.
/// </summary>
internal sealed class GrantAllCapabilityResolver : IEffectiveCapabilityResolver
{
    public Task<bool> HasCapabilityAsync(
        string capability,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(true);
    }
}
