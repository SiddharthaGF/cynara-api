using Microsoft.Extensions.Configuration;

namespace Cynara.Infrastructure.Persistence;

/// <summary>
/// Resolves the active PostgreSQL connection string based on the host
/// environment. <c>ASPNETCORE_ENVIRONMENT=Production</c> selects
/// <c>ConnectionStrings:Prod</c>; any other value selects
/// <c>ConnectionStrings:Default</c>. Env vars
/// (<c>ConnectionStrings__Prod</c>, <c>ConnectionStrings__Default</c>) take
/// precedence over the values defined in <c>appsettings.json</c>.
/// </summary>
public sealed class DatabaseConnectionStringResolver(IConfiguration configuration)
{
    public string Resolve()
    {
        string activeConnection = string.Equals(
            configuration["ASPNETCORE_ENVIRONMENT"],
            "Development",
            StringComparison.Ordinal)
            ? "Development"
            : "Default";

        return configuration.GetConnectionString(activeConnection)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{activeConnection} is required for the PostgreSQL provider.");
    }
}
