namespace Cynara.Api.Tests.Support;

[CollectionDefinition(Name)]
public sealed class MsSqlFixtureDefinition : ICollectionFixture<MsSqlDatabaseFixture>
{
    public const string Name = "MsSql";
}
