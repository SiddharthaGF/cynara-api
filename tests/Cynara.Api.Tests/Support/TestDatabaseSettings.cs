namespace Cynara.Api.Tests.Support;

public sealed record TestDatabaseSettings(string Provider, string ConnectionString)
{
    public static TestDatabaseSettings SqliteInMemory { get; } = new(
        "Sqlite",
        "Data Source=:memory:");

    public bool IsSqlServer =>
        string.Equals(Provider, "SqlServer", StringComparison.OrdinalIgnoreCase);

    public static TestDatabaseSettings SqlServer(string connectionString)
    {
        return new("SqlServer", connectionString);
    }
}
