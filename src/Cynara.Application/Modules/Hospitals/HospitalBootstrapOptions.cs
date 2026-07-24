namespace Cynara.Application.Modules.Hospitals;

/// <summary>
/// Configuration values for hospital workspace bootstrapping. Read from
/// the <c>Hospitals</c> configuration section by the API host and the
/// in-process seed tool.
/// </summary>
public sealed class HospitalBootstrapOptions
{
    /// <summary>Section key under <c>IConfiguration</c>.</summary>
    public const string SectionName = "Hospitals";

    /// <summary>Default header used to resolve the hospital code.</summary>
    public const string DefaultHeaderName = "X-Hospital-Code";

    /// <summary>Code that the bootstrap hospital uses.</summary>
    public string? BootstrapCode { get; set; }

    /// <summary>Display name that the bootstrap hospital uses.</summary>
    public string? BootstrapName { get; set; }

    /// <summary>
    /// Header used to resolve the hospital code. Defaults to
    /// <c>X-Hospital-Code</c> when unset.
    /// </summary>
    public string? HeaderName { get; set; }

    /// <summary>
    /// Whether the bootstrap hospital may be created automatically when
    /// missing. Defaults to <see langword="true"/> for local development; production
    /// deployments are expected to provision hospitals explicitly.
    /// </summary>
    public bool AllowAutoBootstrap { get; set; } = true;
}
