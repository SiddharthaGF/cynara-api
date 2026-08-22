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
            contract = "/schemas/v1",
        });

        _ = endpoints.MapGet("/", () => Results.Text("Hello, Cynara"))
            .AllowAnonymous()
            .ExcludeFromDescription();
        _ = endpoints.MapGet("/health", handler)
            .AllowAnonymous()
            .ExcludeFromDescription();
        _ = endpoints.MapMethods("/health", ["HEAD"], handler)
            .AllowAnonymous()
            .ExcludeFromDescription();

        return endpoints;
    }
}
