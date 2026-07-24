using Cynara.Infrastructure.Persistence;
using Cynara.Infrastructure.Schemas;

namespace Cynara.Api.Modules.Health;

internal static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        _ = endpoints
            .MapGet("/health", HealthAsync)
            .ExcludeFromDescription();

        return endpoints;
    }

    private static async Task<IResult> HealthAsync(
        CynaraDbContext dbContext,
        SchemaFilePaths schemaPaths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(schemaPaths);

        HealthProbe database = await ProbeDatabaseAsync(dbContext, cancellationToken)
            .ConfigureAwait(false);
        HealthProbe schemas = ProbeSchemas(schemaPaths);

        IReadOnlyList<HealthProbe> probes = [database, schemas];
        bool healthy = probes.All(probe => probe.Healthy);
        int statusCode = healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;

        var payload = new
        {
            service = "cynara-api",
            status = healthy ? "ok" : "degraded",
            probes = probes.Select(probe => new
            {
                name = probe.Name,
                status = probe.Healthy ? "ok" : "fail",
                detail = probe.Detail,
            }),
        };

        return Results.Json(payload, statusCode: statusCode);
    }

    private static async Task<HealthProbe> ProbeDatabaseAsync(
        CynaraDbContext dbContext,
        CancellationToken cancellationToken)
    {
        try
        {
            bool canConnect = await dbContext.Database
                .CanConnectAsync(cancellationToken)
                .ConfigureAwait(false);
            return canConnect
                ? HealthProbe.Ok("database")
                : HealthProbe.Fail("database", "Cannot connect");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return HealthProbe.Fail(
                "database",
                $"Cannot connect: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static HealthProbe ProbeSchemas(SchemaFilePaths schemaPaths)
    {
        IReadOnlyList<string> required =
        [
            schemaPaths.ClinicalSchemaPath,
            schemaPaths.UiSchemaPath,
            schemaPaths.RulesSchemaPath,
        ];
        IReadOnlyList<string> missing = [.. required.Where(path => !File.Exists(path))];

        return missing.Count == 0
            ? HealthProbe.Ok("schemas")
            : HealthProbe.Fail(
                "schemas",
                $"Missing {missing.Count} file(s); first missing: {missing[0]}");
    }

    private readonly record struct HealthProbe(string Name, bool Healthy, string? Detail)
    {
        public static HealthProbe Ok(string name)
        {
            return new(name, Healthy: true, Detail: null);
        }

        public static HealthProbe Fail(string name, string detail)
        {
            return new(name, Healthy: false, detail);
        }
    }
}
