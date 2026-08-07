namespace OnlyRag.Infrastructure.Storage.Security;

public interface ISqliteKeyProvider
{
    string GetOrCreateDatabaseKey();
}
