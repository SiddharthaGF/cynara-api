using Microsoft.Extensions.Configuration;

using Npgsql;

namespace Cynara.Infrastructure.Persistence;

/// <summary>
/// Resolves the active PostgreSQL connection string. The application always
/// reads <c>ConnectionStrings:Default</c>; production and Neon PR previews
/// both inject the same setting from the host (Render injects the Neon
/// connection string into <c>ConnectionStrings__Default</c> in both the
/// main service and the preview instances). Env vars take precedence over
/// <c>appsettings.json</c>.
/// </summary>
public sealed class DatabaseConnectionStringResolver(IConfiguration configuration)
{
    public const string DefaultConnectionName = "Default";

    public string Resolve()
    {
        string raw = configuration.GetConnectionString(DefaultConnectionName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{DefaultConnectionName} is required for the PostgreSQL provider.");

        return NormalizeForNpgsql(raw);
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
