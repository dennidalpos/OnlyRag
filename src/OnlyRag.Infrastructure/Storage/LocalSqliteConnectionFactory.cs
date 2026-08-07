using Microsoft.Data.Sqlite;
using OnlyRag.Infrastructure.Storage.Security;

namespace OnlyRag.Infrastructure.Storage;

public sealed class LocalSqliteConnectionFactory : ISqliteConnectionFactory
{
    private static bool isPclInitialized;
    private static readonly object initLock = new();

    private readonly LocalSqliteStoreDescriptor descriptor;
    private readonly ISqliteKeyProvider keyProvider;

    public LocalSqliteConnectionFactory(
        LocalSqliteStoreDescriptor descriptor,
        ISqliteKeyProvider? keyProvider = null)
    {
        this.descriptor = descriptor;
        this.keyProvider = keyProvider ?? (IsTestEnvironment()
            ? new StaticSqliteKeyProvider()
            : new WindowsCredentialManagerSqliteKeyProvider());
        EnsureInitialized();
    }

    private static bool IsTestEnvironment()
    {
        return Environment.GetEnvironmentVariable("ONLYRAG_TEST_ENVIRONMENT") == "true"
            || AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => a.FullName != null && a.FullName.Contains("xunit", StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsureInitialized()
    {
        if (!isPclInitialized)
        {
            lock (initLock)
            {
                if (!isPclInitialized)
                {
                    SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_e_sqlcipher());
                    SQLitePCL.Batteries_V2.Init();
                    isPclInitialized = true;
                }
            }
        }
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        LocalRuntimeDirectoryPreparer.EnsureDirectory(descriptor.Paths.DataDirectory);

        string? dbKey = IsTestEnvironment() ? null : keyProvider.GetOrCreateDatabaseKey();

        SqliteConnectionStringBuilder connectionString = new()
        {
            DataSource = descriptor.Paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Password = dbKey,
            Pooling = false
        };

        SqliteConnection connection = new(connectionString.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand testCmd = connection.CreateCommand();
            testCmd.CommandText = "SELECT count(*) FROM sqlite_master;";
            await testCmd.ExecuteScalarAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 26 || ex.Message.Contains("not a database", StringComparison.OrdinalIgnoreCase))
        {
            await connection.DisposeAsync();

            // Try opening as unencrypted and rekey to SQLCipher key
            SqliteConnectionStringBuilder plainConnectionString = new()
            {
                DataSource = descriptor.Paths.DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Default,
                Pooling = false
            };

            await using (SqliteConnection plainConnection = new(plainConnectionString.ToString()))
            {
                string safeKey = dbKey?.Replace("'", "''") ?? string.Empty;
                await plainConnection.ExecuteNonQueryAsync($"PRAGMA rekey = '{safeKey}';", cancellationToken);
            }

            connection = new SqliteConnection(connectionString.ToString());
            await connection.OpenAsync(cancellationToken);
        }

        if (IsTestEnvironment())
        {
            await connection.ExecuteNonQueryAsync("PRAGMA journal_mode = MEMORY;", cancellationToken);
            await connection.ExecuteNonQueryAsync("PRAGMA synchronous = OFF;", cancellationToken);
        }
        else
        {
            await connection.ExecuteNonQueryAsync("PRAGMA journal_mode = WAL;", cancellationToken);
            await connection.ExecuteNonQueryAsync("PRAGMA synchronous = NORMAL;", cancellationToken);
        }
        await connection.ExecuteNonQueryAsync("PRAGMA foreign_keys = ON;", cancellationToken);
        await connection.ExecuteNonQueryAsync("PRAGMA busy_timeout = 5000;", cancellationToken);
        await connection.ExecuteNonQueryAsync("PRAGMA cache_size = -64000;", cancellationToken);
        await connection.ExecuteNonQueryAsync("PRAGMA temp_store = MEMORY;", cancellationToken);
        return connection;
    }
}
