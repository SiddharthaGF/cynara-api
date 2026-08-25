using System.Net;

using Cynara.Api.Common.ErrorHandling;
using Cynara.Application;
using Cynara.Application.Modules.Capabilities;

using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Middleware;
using JsonApiDotNetCore.Serialization.Objects;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Api.JsonApi;

/// <summary>
/// Maps application <see cref="CynaraException"/> types and raw EF
/// optimistic-concurrency conflicts (<c>DbUpdateConcurrencyException</c>) to
/// JSON:API error objects with the correct HTTP status instead of treating
/// them as 500s.
/// The status code, title, detail, code, and pointer all originate in the
/// shared <see cref="CynaraErrorMapping"/> so both this JsonAPI handler and
/// the minimal-API <c>ProblemDetailsMapping</c> emit byte-identical error
/// documents for the same exception. Denied access
/// (<see cref="CapabilityForbiddenException"/>) is additionally recorded in
/// the audit trail.
/// </summary>
internal sealed class CynaraJsonApiExceptionHandler(
    ILoggerFactory loggerFactory,
    IJsonApiOptions options,
    IDeniedAccessAuditor deniedAccessAuditor,
    IHttpContextAccessor httpContextAccessor)
    : ExceptionHandler(loggerFactory, options)
{
    protected override IReadOnlyList<ErrorObject> CreateErrorResponse(
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is CapabilityForbiddenException forbidden)
        {
            CancellationToken abortToken =
                httpContextAccessor.HttpContext?.RequestAborted
                ?? CancellationToken.None;

            deniedAccessAuditor.RecordAsync(
                forbidden.Capability,
                forbidden.ActorId,
                httpContextAccessor.HttpContext?.Request.Path,
                abortToken).GetAwaiter().GetResult();
        }

        if (exception is not CynaraException
            and not DbUpdateConcurrencyException)
        {
            return base.CreateErrorResponse(exception);
        }

        CynaraErrorDocument document = CynaraErrorMapping.FromException(exception);

        return [.. document.Items
            .Select(item => new ErrorObject((HttpStatusCode)document.StatusCode)
            {
                Code = item.Code,
                Title = item.Title,
                Detail = item.Detail,
                Source = item.Source == null
                    ? null
                    : new ErrorSource { Pointer = item.Source.JsonApiPointer },
            })];
    }
}
