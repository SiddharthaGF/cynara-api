using Cynara.Api.Endpoints;
using Cynara.Application;
using Cynara.Infrastructure;
using Cynara.Infrastructure.Schemas;

using Microsoft.AspNetCore.Diagnostics;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string databaseProvider = builder.Configuration["Database:Provider"]
    ?? DependencyInjection.SqliteProvider;
string connectionString = builder.Configuration.GetConnectionString("Default")
    ?? (DependencyInjection.IsSqlServer(databaseProvider)
        ? throw new InvalidOperationException(
            "ConnectionStrings:Default is required when Database:Provider is SqlServer.")
        : "Data Source=cynara.db");

string schemaRoot = Path.Combine(AppContext.BaseDirectory, "Schemas");
var schemaPaths = new SchemaFilePaths
{
    ClinicalSchemaPath = Path.Combine(schemaRoot, "v1", "clinical-schema.schema.json"),
    UiSchemaPath = Path.Combine(schemaRoot, "v1", "ui-schema.schema.json"),
    RulesSchemaPath = Path.Combine(schemaRoot, "v1", "rules-schema.schema.json"),
};

builder.Services.AddOpenApi();
builder.Services.AddCynaraInfrastructure(
    connectionString,
    schemaPaths,
    databaseProvider);
builder.Services.AddSingleton(TimeProvider.System);

WebApplication app = builder.Build();

app.UseExceptionHandler(exceptionHandler =>
{
    exceptionHandler.Run(async context =>
    {
        IExceptionHandlerFeature? feature = context.Features.Get<IExceptionHandlerFeature>();
        if (feature?.Error is CynaraException cynaraException)
        {
            IResult result = ProblemDetailsMapping.FromException(cynaraException);
            await result.ExecuteAsync(context);
            return;
        }

        await Results.Problem(
            detail: feature?.Error.Message,
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Unexpected error").ExecuteAsync(context);
    });
});

await app.Services.InitializeDatabaseAsync();

if (app.Environment.IsDevelopment())
{
    _ = app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new
{
    service = "cynara-api",
    status = "ok",
    contract = "https://github.com/ailuracode/cynara",
}));

app.MapComponentEndpoints();
app.MapFormEndpoints();
app.MapFormResponseEndpoints();
app.MapAuditEndpoints();

app.Run();

public partial class Program;
