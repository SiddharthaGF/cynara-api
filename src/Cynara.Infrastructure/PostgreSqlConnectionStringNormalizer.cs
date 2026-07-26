namespace Cynara.Infrastructure;

internal static class PostgreSqlConnectionStringNormalizer
{
    public static string Normalize(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        if (!IsPostgreSqlUrl(connectionString))
        {
            return connectionString;
        }

        if (!Uri.TryCreate(connectionString, UriKind.Absolute, out Uri? uri)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException(
                "The PostgreSQL connection URL is invalid.");
        }

        var builder = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
        };

        ApplyCredentials(builder, uri.UserInfo);
        ApplyQuery(builder, uri.Query);
        return builder.ConnectionString;
    }

    private static bool IsPostgreSqlUrl(string connectionString)
    {
        return connectionString.StartsWith(
                "postgres://",
                StringComparison.OrdinalIgnoreCase)
            || connectionString.StartsWith(
                "postgresql://",
                StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyCredentials(
        Npgsql.NpgsqlConnectionStringBuilder builder,
        string userInfo)
    {
        int separator = userInfo.IndexOf(':', StringComparison.Ordinal);
        string username = separator >= 0 ? userInfo[..separator] : userInfo;
        builder.Username = Uri.UnescapeDataString(username);

        if (separator >= 0)
        {
            builder.Password = Uri.UnescapeDataString(userInfo[(separator + 1)..]);
        }
    }

    private static void ApplyQuery(
        Npgsql.NpgsqlConnectionStringBuilder builder,
        string query)
    {
        foreach (string parameter in query.TrimStart('?').Split(
                     '&',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = parameter.IndexOf('=', StringComparison.Ordinal);
            string key = DecodeQueryPart(
                separator >= 0 ? parameter[..separator] : parameter);
            string value = DecodeQueryPart(
                separator >= 0 ? parameter[(separator + 1)..] : string.Empty);

            ApplyQueryParameter(builder, key, value);
        }
    }

    private static void ApplyQueryParameter(
        Npgsql.NpgsqlConnectionStringBuilder builder,
        string key,
        string value)
    {
        string normalizedKey = key
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        switch (normalizedKey)
        {
            case "sslmode":
                builder.SslMode = ParseSslMode(value);
                break;
            case "channelbinding":
                builder.ChannelBinding = ParseChannelBinding(value);
                break;
            default:
                builder[key.Replace('_', ' ')] = value;
                break;
        }
    }

    private static Npgsql.SslMode ParseSslMode(string value)
    {
        string normalized = value.Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        return normalized switch
        {
            "disable" => Npgsql.SslMode.Disable,
            "allow" => Npgsql.SslMode.Allow,
            "prefer" => Npgsql.SslMode.Prefer,
            "verifyca" => Npgsql.SslMode.VerifyCA,
            "verifyfull" => Npgsql.SslMode.VerifyFull,
            "" or "require" => Npgsql.SslMode.Require,
            _ => throw new InvalidOperationException(
                "The PostgreSQL connection URL contains an invalid sslmode."),
        };
    }

    private static Npgsql.ChannelBinding ParseChannelBinding(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "disable" => Npgsql.ChannelBinding.Disable,
            "prefer" => Npgsql.ChannelBinding.Prefer,
            "" or "require" => Npgsql.ChannelBinding.Require,
            _ => throw new InvalidOperationException(
                "The PostgreSQL connection URL contains invalid channel_binding."),
        };
    }

    private static string DecodeQueryPart(string value)
    {
        return Uri.UnescapeDataString(value.Replace('+', ' '));
    }
}
