using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Cynara.Api.CapabilityAuthorization;

/// <summary>
/// Synthesizes a policy for any capability name on demand, so the capability
/// catalog does not have to be enumerated at startup and future codes keep
/// working without registration. Each policy carries a single
/// <see cref="CapabilityRequirement"/>; the built-in default and fallback
/// policies are delegated to the framework default provider.
/// </summary>
public sealed class CapabilityPolicyProvider(
    IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider fallback = new(options);

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        ArgumentNullException.ThrowIfNull(policyName);

        AuthorizationPolicy policy = new AuthorizationPolicyBuilder()
            .AddRequirements(new CapabilityRequirement(policyName))
            .Build();
        return Task.FromResult<AuthorizationPolicy?>(policy);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync()
    {
        return fallback.GetDefaultPolicyAsync();
    }

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync()
    {
        return fallback.GetFallbackPolicyAsync();
    }
}
