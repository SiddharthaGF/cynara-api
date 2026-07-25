namespace Cynara.Api.Tests.Support;

public sealed record TestDatabaseSettings(string ConnectionString)
{
    public static TestDatabaseSettings FromConnectionString(string connectionString)
    {
        return new(connectionString);
    }
}
