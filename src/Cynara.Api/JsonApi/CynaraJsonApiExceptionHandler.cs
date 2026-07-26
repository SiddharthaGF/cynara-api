using System.Net;

using Cynara.Api.Common.ErrorHandling;
using Cynara.Application;

using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Middleware;
using JsonApiDotNetCore.Serialization.Objects;

namespace Cynara.Api.JsonApi;

/// <summary>
/// Maps application <see cref="CynaraException"/> types to JSON:API error
/// objects with the correct HTTP status instead of treating them as 500s.
/// The status code, title, detail, code, and pointer all originate in the
/// shared <see cref="CynaraErrorMapping"/> so both this JsonAPI handler and
/// the minimal-API <c>ProblemDetailsMapping</c> emit byte-identical error
/// documents for the same exception.
/// </summary>
internal sealed class CynaraJsonApiExceptionHandler(
    ILoggerFactory loggerFactory,
    IJsonApiOptions options)
    : ExceptionHandler(loggerFactory, options)
{
    protected override IReadOnlyList<ErrorObject> CreateErrorResponse(
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is not CynaraException)
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
