using Microsoft.AspNetCore.Authorization;

namespace Cynara.Api.CapabilityAuthorization;

/// <summary>
/// Authorization requirement that passes when the current actor holds the
/// named capability in the resolved hospital workspace. Evaluated by
/// <see cref="CapabilityAuthorizationHandler"/> through the native
/// authorization pipeline; the policy itself is synthesized on demand by
/// <see cref="CapabilityPolicyProvider"/>.
/// </summary>
public sealed class CapabilityRequirement(string capability)
    : IAuthorizationRequirement
{
    public string Capability { get; } = capability;
}
