using Cynara.Application.Modules.Capabilities;

namespace Cynara.Api.Tests.Support;

/// <summary>
/// Permissive capability guard for unit tests. Allows every requested
/// capability by default so tests focus on service behavior; construct it
/// with a restricted set to exercise denial paths.
/// </summary>
internal sealed class FakeCapabilityGuard(
    IReadOnlySet<string>? allowed = null) : ICapabilityGuard
{
    public IReadOnlySet<string>? Allowed { get; } = allowed;

    public async Task RequireAsync(
        string capability,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        if (Allowed?.Contains(capability) != false)
        {
            return;
        }

        throw new CapabilityForbiddenException(capability, actorId: null);
    }
}
