using OnlyRag.Infrastructure.Storage.Security;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public sealed class SqliteEncryptionTests
{
    [Fact]
    public void WindowsCredentialManagerSqliteKeyProvider_ReturnsValid256BitKey()
    {
        var keyProvider = new WindowsCredentialManagerSqliteKeyProvider();

        string key = keyProvider.GetOrCreateDatabaseKey();

        Assert.NotNull(key);
        Assert.True(key.Length >= 64, "Database encryption key must be at least 64 hex characters (256-bit).");
    }

    [Fact]
    public void WindowsCredentialManagerSqliteKeyProvider_ReturnsSameKeyOnRepeatedCalls()
    {
        var keyProvider = new WindowsCredentialManagerSqliteKeyProvider();

        string key1 = keyProvider.GetOrCreateDatabaseKey();
        string key2 = keyProvider.GetOrCreateDatabaseKey();

        Assert.Equal(key1, key2);
    }
}
