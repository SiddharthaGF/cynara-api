using Cynara.Infrastructure;

namespace Cynara.Api.Tests;

public sealed class PostgreSqlConnectionStringNormalizerTests
{
    [Fact]
    public void AddCynaraDatabase_UsesNormalizedConnectionString()
    {
        var services = new ServiceCollection();
        _ = services.AddCynaraDatabase(
            "postgresql://user@db.example.com/neondb?sslmode");

        using ServiceProvider provider = services.BuildServiceProvider();
        using CynaraDbContext dbContext = provider
            .GetRequiredService<CynaraDbContext>();
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(
            dbContext.Database.GetConnectionString());

        Assert.Equal("db.example.com", builder.Host);
        Assert.Equal("neondb", builder.Database);
        Assert.Equal(Npgsql.SslMode.Require, builder.SslMode);
    }

    [Fact]
    public void Normalize_PreservesNpgsqlConnectionString()
    {
        const string ConnectionString =
            "Host=localhost;Port=5432;Database=cynara;Username=user;Password=password";

        string result = PostgreSqlConnectionStringNormalizer.Normalize(ConnectionString);

        Assert.Equal(ConnectionString, result);
    }

    [Fact]
    public void Normalize_ConvertsNeonUrlToNpgsqlConnectionString()
    {
        const string ConnectionUrl =
            "postgresql://user%40tenant@db.example.com:5433/neondb"
            + "?sslmode=require&channel_binding=prefer&application_name=cynara-api";

        string result = PostgreSqlConnectionStringNormalizer.Normalize(ConnectionUrl);
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(result);

        Assert.Equal("db.example.com", builder.Host);
        Assert.Equal(5433, builder.Port);
        Assert.Equal("neondb", builder.Database);
        Assert.Equal("user@tenant", builder.Username);
        Assert.Null(builder.Password);
        Assert.Equal(Npgsql.SslMode.Require, builder.SslMode);
        Assert.Equal(Npgsql.ChannelBinding.Prefer, builder.ChannelBinding);
        Assert.Equal("cynara-api", builder.ApplicationName);
    }

    [Fact]
    public void Normalize_DefaultsTruncatedSslModeToRequire()
    {
        const string ConnectionUrl =
            "postgresql://user@db.example.com/neondb?sslmode";

        string result = PostgreSqlConnectionStringNormalizer.Normalize(ConnectionUrl);
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(result);

        Assert.Equal(5432, builder.Port);
        Assert.Equal(Npgsql.SslMode.Require, builder.SslMode);
    }

    [Fact]
    public void Normalize_RejectsInvalidSslModeWithoutExposingConnectionUrl()
    {
        const string ConnectionUrl =
            "postgresql://user@db.example.com/neondb?sslmode=invalid";

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => PostgreSqlConnectionStringNormalizer.Normalize(ConnectionUrl));

        Assert.DoesNotContain(
            "db.example.com",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains("sslmode", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
