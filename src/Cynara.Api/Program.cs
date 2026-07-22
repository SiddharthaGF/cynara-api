using Cynara.Api.Common.ErrorHandling;
using Cynara.Api.Modules.Audit;
using Cynara.Api.Modules.Components;
using Cynara.Api.Modules.FormAi;
using Cynara.Api.Modules.FormResponses;
using Cynara.Api.Modules.Forms;
using Cynara.Api.Modules.Health;
using Cynara.Application;
using Cynara.Infrastructure;
using Cynara.Infrastructure.Modules.Preview;
using Cynara.Infrastructure.Schemas;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

bool previewStorage = InfrastructureServiceCollectionExtensions.IsPreviewStorage(builder.Configuration);
string databaseProvider = previewStorage
    ? InfrastructureServiceCollectionExtensions.SqliteProvider
    : builder.Configuration["Database:Provider"]
        ?? InfrastructureServiceCollectionExtensions.SqliteProvider;
string connectionString = previewStorage
    ? "Data Source=:memory:"
    : builder.Configuration.GetConnectionString("Default")
        ?? (InfrastructureServiceCollectionExtensions.IsSqlServer(databaseProvider)
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
string[] allowedCorsOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()
    ?? [];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(
        policy => policy
            .WithOrigins(allowedCorsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod());
});
builder.Services.AddCynaraApplication();
builder.Services.AddCynaraInfrastructure(
    connectionString,
    schemaPaths,
    databaseProvider);
builder.Services.AddSingleton(TimeProvider.System);

WebApplication app = builder.Build();

_ = app.UseCynaraExceptionHandling();
_ = app.UseCors();

await app.Services.InitializeDatabaseAsync().ConfigureAwait(false);
if (previewStorage)
{
    await app.Services.SeedPreviewDemoAsync().ConfigureAwait(false);
}

if (app.Environment.IsDevelopment())
{
    _ = app.MapOpenApi();
}

app.MapComponentEndpoints();
app.MapFormEndpoints();
app.MapFormResponseEndpoints();
app.MapFormAiEndpoints();
app.MapAuditEndpoints();
app.MapHealthEndpoints();
app.MapGet("/", () => Results.Text("Cynara API"));

if (string.Equals(
        builder.Configuration["CYNARA_ENABLE_TEST_ENDPOINTS"],
        "true",
        StringComparison.OrdinalIgnoreCase))
{
    _ = app.MapGet("/test/throw-unhandled", () =>
    {
        throw new InvalidOperationException("boom-unhandled");
    });
    _ = app.MapGet("/test/throw-validation", () =>
    {
        throw new ValidationException("validation failure");
    });
}

await app.RunAsync().ConfigureAwait(false);

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "WebApplicationFactory requires the generated host type to be public.")]
public partial class Program;
