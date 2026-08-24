using Cynara.Application.Modules.Hospitals;

namespace Cynara.Api.Tests.Support;

/// <summary>
/// Optional configuration for <see cref="CynaraWebApplicationFactory"/>;
/// unset members keep the factory defaults.
/// </summary>
internal sealed class CynaraWebApplicationFactoryOptions
{
    /// <summary>Overrides the bootstrap hospital definition.</summary>
    public HospitalBootstrapOptions? BootstrapOptions { get; init; }

    /// <summary>Sets RENDER_SERVICE_TYPE=web to emulate Render proxies.</summary>
    public bool EmulateRenderProxy { get; init; }

    /// <summary>
    /// Replaces the effective-capability resolver with a grant-all fake.
    /// </summary>
    public bool GrantAllCapabilities { get; init; } = true;

    /// <summary>Disables the header-driven test authentication seam.</summary>
    public bool UseRealAuthentication { get; init; }

    /// <summary>Overrides ASPNETCORE_ENVIRONMENT for the test host.</summary>
    public string? EnvironmentName { get; init; }

    /// <summary>Certificate paths for real OpenIddict token signing.</summary>
    public TestOpenIddictCertificates? OpenIddictCertificates { get; init; }

    /// <summary>Extra in-memory configuration values for the test host.</summary>
    public IReadOnlyDictionary<string, string?>? ExtraConfiguration { get; init; }
}
