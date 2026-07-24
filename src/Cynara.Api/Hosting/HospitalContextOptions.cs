using Cynara.Application.Modules.Hospitals;

namespace Cynara.Api.Hosting;

/// <summary>
/// Configuration for the hospital context middleware. Bound from the
/// <c>Hospitals</c> section; the <see cref="HeaderName"/> defaults to
/// <c>X-Hospital-Code</c>.
/// </summary>
public sealed class HospitalContextOptions
{
    /// <summary>Default header used when no override is configured.</summary>
    public const string DefaultHeaderName = HospitalBootstrapOptions.DefaultHeaderName;

    /// <summary>Header that the middleware reads to resolve the hospital code.</summary>
    public string HeaderName { get; set; } = DefaultHeaderName;
}
