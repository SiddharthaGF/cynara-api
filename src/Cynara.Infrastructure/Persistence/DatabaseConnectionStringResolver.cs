using Microsoft.Extensions.Configuration;

namespace Cynara.Infrastructure.Persistence;

/// <summary>
/// Resolves the active PostgreSQL connection string. Resolution order:
/// <list type="number">
///   <item><c>ConnectionStrings:ActiveName</c> — explicit named connection
///     (used by Neon PR previews and production deploys to pick a named
///     connection without changing <c>ASPNETCORE_ENVIRONMENT</c>).</item>
///   <item>Otherwise, <c>ConnectionStrings:Default</c> (preserves the
///     pre-existing behaviour for the integration test suite, which sets
///     <c>ConnectionStrings:Default</c> in memory and never sets
///     <c>ASPNETCORE_ENVIRONMENT</c>).</item>
/// </list>
/// Env vars (<c>ConnectionStrings__Default</c>, <c>ConnectionStrings__Prod</c>,
/// <c>ConnectionStrings__ActiveName</c>) take precedence over
/// <c>appsettings.json</c>.
/// </summary>
public sealed class DatabaseConnectionStringResolver(IConfiguration configuration)
{
    public const string ActiveNameSetting = "ConnectionStrings:ActiveName";
    public const string DefaultConnectionName = "Default";
    public const string ProductionConnectionName = "Prod";

    public string Resolve()
    {
        string activeConnection = ResolveActiveConnectionName();

        return configuration.GetConnectionString(activeConnection)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{activeConnection} is required for the PostgreSQL provider.");
    }

    private string ResolveActiveConnectionName()
    {
        string? explicitName = configuration[ActiveNameSetting];
        if (!string.IsNullOrWhiteSpace(explicitName))
        {
            return explicitName;
        }

        return DefaultConnectionName;
    }
}
