namespace Cynara.Api.Tests.Support;

[CollectionDefinition(Name)]
public sealed class PostgresFixtureDefinition : ICollectionFixture<PostgreSqlDatabaseFixture>
{
    public const string Name = "Postgres";
}
