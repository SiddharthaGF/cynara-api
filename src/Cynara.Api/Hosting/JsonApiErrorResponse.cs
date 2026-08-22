namespace Cynara.Api.Hosting;

internal static class JsonApiErrorResponse
{
    private const string ContentType = "application/vnd.api+json";

    public static async Task WriteAsync(
        HttpContext context,
        int statusCode,
        string title,
        string detail)
    {
        var document = new
        {
            errors = new[]
            {
                new
                {
                    status = statusCode.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    title,
                    detail,
                },
            },
        };

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = ContentType;
        await context.Response.WriteAsJsonAsync(
            document,
            options: null,
            contentType: ContentType,
            cancellationToken: context.RequestAborted).ConfigureAwait(false);
    }
}
