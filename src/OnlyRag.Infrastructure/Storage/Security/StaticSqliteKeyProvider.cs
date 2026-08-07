namespace OnlyRag.Infrastructure.Storage.Security;

public sealed class StaticSqliteKeyProvider : ISqliteKeyProvider
{
    public string GetOrCreateDatabaseKey() => "onlyrag-static-test-key-32-bytes";
}
