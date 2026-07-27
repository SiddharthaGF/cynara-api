namespace Cynara.Api.Modules.Health;

internal static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        Func<IResult> handler = () => Results.Ok(new
        {
            service = "cynara-api",
            status = "ok",
            contract = "https://github.com/ailuracode/cynara",
        });

        _ = endpoints.MapGet("/", () => Results.Text("Hello, Cynara"))
            .ExcludeFromDescription();
        _ = endpoints.MapGet("/health", handler).ExcludeFromDescription();
        _ = endpoints.MapMethods("/health", ["HEAD"], handler).ExcludeFromDescription();

        return endpoints;
    }
}
