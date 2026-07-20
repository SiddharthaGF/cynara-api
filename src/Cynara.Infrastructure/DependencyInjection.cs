using System.Data.Common;

using Cynara.Application.Audit;
using Cynara.Application.Components;
using Cynara.Application.Forms;
using Cynara.Application.Persistence;
using Cynara.Application.Schemas;
using Cynara.Infrastructure.Persistence;
using Cynara.Infrastructure.Schemas;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Infrastructure;

public static class DependencyInjection
{
    private static readonly string[] RequiredTables =
    [
        "audit_events",
        "component_definitions",
        "component_versions",
        "form_definitions",
        "form_versions",
        "form_responses",
        "form_response_revisions",
    ];

    public static IServiceCollection AddCynaraInfrastructure(
        this IServiceCollection services,
        string connectionString,
        SchemaFilePaths schemaPaths)
    {
        _ = services.AddSingleton(schemaPaths);
        _ = services.AddSingleton<ISchemaValidator, JsonSchemaValidator>();
        _ = services.AddDbContext<CynaraDbContext>(options => options.UseSqlite(connectionString));
        _ = services.AddScoped<IComponentRepository, ComponentRepository>();
        _ = services.AddScoped<IFormRepository, FormRepository>();
        _ = services.AddScoped<IFormResponseRepository, FormResponseRepository>();
        _ = services.AddScoped<IAuditRepository, AuditRepository>();
        _ = services.AddScoped<IAuditService, AuditService>();
        _ = services.AddScoped<IComponentService, ComponentService>();
        _ = services.AddScoped<IFormCompiler, FormCompiler>();
        _ = services.AddSingleton<IFormRuleEngine, FormRuleEngine>();
        _ = services.AddScoped<IFormService, FormService>();
        _ = services.AddScoped<IFormResponseValidator, FormResponseValidator>();
        _ = services.AddScoped<IFormResponseService, FormResponseService>();
        return services;
    }

    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider.GetRequiredService<CynaraDbContext>();
        bool created = await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        if (!created && !await AllRequiredTablesExistAsync(dbContext, cancellationToken))
        {
            _ = await dbContext.Database.EnsureDeletedAsync(cancellationToken);
            _ = await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }
    }

    private static async Task<bool> AllRequiredTablesExistAsync(
        CynaraDbContext dbContext,
        CancellationToken cancellationToken)
    {
        DbConnection connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
            var existingTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                _ = existingTables.Add(reader.GetString(0));
            }

            return RequiredTables.All(existingTables.Contains);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }
}
