using Microsoft.Extensions.Configuration;

using Npgsql;

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

        string raw = configuration.GetConnectionString(activeConnection)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{activeConnection} is required for the PostgreSQL provider.");

        return NormalizeForNpgsql(raw);
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

    private static string NormalizeForNpgsql(string raw)
    {
        if (!raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
            && !raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
        {
            return raw;
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri))
        {
            return raw;
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(uri.UserInfo.Split(':', 2)[0]),
            Password = uri.UserInfo.Contains(':')
                ? Uri.UnescapeDataString(uri.UserInfo.Split(':', 2)[1])
                : string.Empty,
        };

        foreach (string param in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = param.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            string key = param[..eq].ToLowerInvariant();
            string value = Uri.UnescapeDataString(param[(eq + 1)..]);

#pragma warning disable IDE0010 // Add missing cases
            switch (key)
            {
                case "sslmode":
                    builder.SslMode = value.ToLowerInvariant() switch
                    {
                        "disable" => SslMode.Disable,
                        "allow" => SslMode.Allow,
                        "prefer" => SslMode.Prefer,
                        "require" => SslMode.Require,
                        "verify-ca" => SslMode.VerifyCA,
                        "verify-full" => SslMode.VerifyFull,
                        _ => builder.SslMode,
                    };
                    break;
                case "channel_binding":
                    builder.ChannelBinding = value.ToLowerInvariant() switch
                    {
                        "disable" => ChannelBinding.Disable,
                        "prefer" => ChannelBinding.Prefer,
                        "require" => ChannelBinding.Require,
                        _ => builder.ChannelBinding,
                    };
                    break;
                case "pooling":
                    builder.Pooling = !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
                    break;
            }
#pragma warning restore IDE0010 // Add missing cases
        }

        return builder.ConnectionString;
    }
}
