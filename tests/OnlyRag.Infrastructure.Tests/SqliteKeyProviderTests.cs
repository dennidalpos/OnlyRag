using System;
using OnlyRag.Infrastructure.Storage.Security;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public class SqliteKeyProviderTests
{
    [Fact]
    public void GetOrCreateDatabaseKey_ReturnsNonEmpty64CharHexKey()
    {
        WindowsCredentialManagerSqliteKeyProvider keyProvider = new();
        string key1 = keyProvider.GetOrCreateDatabaseKey();

        Assert.NotNull(key1);
        Assert.Equal(64, key1.Length);

        string key2 = keyProvider.GetOrCreateDatabaseKey();
        Assert.Equal(key1, key2);
    }
}
