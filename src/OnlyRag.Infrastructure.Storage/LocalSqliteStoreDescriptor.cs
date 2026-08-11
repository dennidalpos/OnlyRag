using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure;

public sealed record LocalSqliteStoreDescriptor(AppStoragePaths Paths)
{
    public string ProviderName => "SQLite";

    public static LocalSqliteStoreDescriptor CreateDefault()
    {
        return new LocalSqliteStoreDescriptor(AppStoragePaths.FromLocalAppData());
    }
}
