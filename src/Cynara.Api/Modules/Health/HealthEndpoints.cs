namespace Cynara.Api.Modules.Health;

internal static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        _ = endpoints
            .MapGet("/health", () => Results.Ok(new
            {
                service = "cynara-api",
                status = "ok",
                contract = "https://github.com/ailuracode/cynara",
            }))
            .ExcludeFromDescription();

        return endpoints;
    }
}
