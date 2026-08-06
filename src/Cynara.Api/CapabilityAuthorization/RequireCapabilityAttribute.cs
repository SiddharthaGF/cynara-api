namespace Cynara.Api.CapabilityAuthorization;

/// <summary>
/// Declares the capability required by a controller action (or an entire
/// controller). The global <see cref="CapabilityAuthorizationFilter"/>
/// enforces it at the endpoint boundary before model binding or action
/// invocation runs, so a denied request never reaches a workflow.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method,
    AllowMultiple = false,
    Inherited = true)]
public sealed class RequireCapabilityAttribute(string capability)
    : Attribute
{
    public string Capability { get; } = capability;
}
