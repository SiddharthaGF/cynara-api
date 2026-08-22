using Cynara.Application.Modules.Hospitals;

namespace Cynara.Api.Hosting;

internal static class HospitalContextMiddlewareExtensions
{
    public static IServiceCollection AddCynaraHospitalContext(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        HospitalContextOptions options = new();
        configuration
            .GetSection(HospitalBootstrapOptions.SectionName)
            .Bind(options);
        if (string.IsNullOrWhiteSpace(options.HeaderName))
        {
            options.HeaderName = HospitalContextOptions.DefaultHeaderName;
        }

        _ = services.AddSingleton(
            Microsoft.Extensions.Options.Options.Create(options));
        return services;
    }

    public static IApplicationBuilder UseHospitalContext(
        this IApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseMiddleware<HospitalContextMiddleware>();
    }

    public static IApplicationBuilder UseMembershipResolution(
        this IApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseMiddleware<MembershipResolutionMiddleware>();
    }
}
