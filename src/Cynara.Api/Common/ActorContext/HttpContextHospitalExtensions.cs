using Cynara.Application.Modules.Hospitals;

namespace Cynara.Api.Common.ActorContext;

internal static class HttpContextHospitalExtensions
{
    /// <summary>
    /// Default request header used to resolve the hospital workspace. Tests
    /// and the API host can override it through the
    /// <c>Hospitals:HeaderName</c> configuration value.
    /// </summary>
    public const string DefaultHeaderName = HospitalBootstrapOptions.DefaultHeaderName;

    public static string? GetHospitalCode(this HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return httpContext.Request.Headers.TryGetValue(
                DefaultHeaderName,
                out Microsoft.Extensions.Primitives.StringValues value)
            ? value.ToString()
            : null;
    }

    public static string? GetHospitalCode(
        this HttpContext httpContext,
        string headerName)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (string.IsNullOrWhiteSpace(headerName))
        {
            return null;
        }

        return httpContext.Request.Headers.TryGetValue(
                headerName,
                out Microsoft.Extensions.Primitives.StringValues value)
            ? value.ToString()
            : null;
    }
}
