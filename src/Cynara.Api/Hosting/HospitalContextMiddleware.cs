using Cynara.Api.Common.ActorContext;
using Cynara.Application.Modules.Hospitals;

using Microsoft.Extensions.Options;

namespace Cynara.Api.Hosting;

/// <summary>
/// Resolves the hospital workspace for every request that requires tenant
/// context. Reads the configured header (defaults to
/// <c>X-Hospital-Code</c>), looks up the hospital by code, and stamps the
/// scoped <see cref="IHospitalContext"/> for the request. Anonymous
/// requests are rejected with a 400/403 result when the path is tenant-owned.
/// The bearer-only membership listing is tenant-exempt and passes through
/// without a hospital header.
/// </summary>
internal sealed partial class HospitalContextMiddleware
{
    private const string HospitalContextItemsKey = "Cynara.HospitalContext";

    private readonly RequestDelegate next;
    private readonly ILogger<HospitalContextMiddleware> logger;
    private readonly HospitalContextOptions options;

    public HospitalContextMiddleware(
        RequestDelegate next,
        ILogger<HospitalContextMiddleware> logger,
        IOptions<HospitalContextOptions> options)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        this.next = next;
        this.logger = logger;
        this.options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (AuthPathPolicy.IsPublicPath(context.Request.Path)
            || AuthPathPolicy.IsTenantExemptPath(context.Request.Path))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        string? code = context.GetHospitalCode(options.HeaderName);
        if (string.IsNullOrWhiteSpace(code))
        {
            await WriteTenantErrorAsync(
                context,
                StatusCodes.Status400BadRequest,
                "Hospital context required",
                $"Missing {options.HeaderName} header. Provide a known hospital code.")
                .ConfigureAwait(false);
            return;
        }

        HospitalContext hospitalContext = context.RequestServices
            .GetRequiredService<HospitalContext>();

        Domain.Hospitals.Hospital? hospital = await context.RequestServices
            .GetRequiredService<IHospitalRepository>()
            .FindByCodeAsync(code, context.RequestAborted)
            .ConfigureAwait(false);

        if (hospital is null)
        {
            await WriteTenantErrorAsync(
                context,
                StatusCodes.Status400BadRequest,
                "Unknown hospital workspace",
                $"Hospital '{code}' was not found.")
                .ConfigureAwait(false);
            return;
        }

        if (hospital.Status != Domain.Hospitals.HospitalStatus.Active)
        {
            await WriteTenantErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                "Hospital workspace unavailable",
                $"Hospital '{code}' is {hospital.Status.ToString().ToLowerInvariant()}.")
                .ConfigureAwait(false);
            return;
        }

        hospitalContext.SetWorkspace(hospital.Id, hospital.Code, hospital.Name);
        context.Items[HospitalContextItemsKey] = true;

        await next(context).ConfigureAwait(false);
    }

    private async Task WriteTenantErrorAsync(
        HttpContext context,
        int statusCode,
        string title,
        string detail)
    {
        LogRejectingRequest(logger, context.Request.Method, context.Request.Path, title);

        var document = new
        {
            errors = new[]
            {
                new
                {
                    status = statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    title,
                    detail,
                },
            },
        };

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/vnd.api+json";
        await context.Response.WriteAsJsonAsync(
            document,
            options: null,
            contentType: "application/vnd.api+json",
            cancellationToken: context.RequestAborted).ConfigureAwait(false);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Rejecting {Method} {Path}: {Title}")]
    private static partial void LogRejectingRequest(
        ILogger logger,
        string method,
        PathString path,
        string title);
}
