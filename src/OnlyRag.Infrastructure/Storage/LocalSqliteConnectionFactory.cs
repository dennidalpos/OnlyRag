using Microsoft.Data.Sqlite;

namespace OnlyRag.Infrastructure.Storage;

public sealed class LocalSqliteConnectionFactory : ISqliteConnectionFactory
{
    private readonly LocalSqliteStoreDescriptor descriptor;

    public LocalSqliteConnectionFactory(LocalSqliteStoreDescriptor descriptor)
    {
        this.descriptor = descriptor;
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(descriptor.Paths.DataDirectory);

        SqliteConnectionStringBuilder connectionString = new()
        {
            DataSource = descriptor.Paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = false
        };

        SqliteConnection connection = new(connectionString.ToString());
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteNonQueryAsync("PRAGMA journal_mode = WAL;", cancellationToken);
        await connection.ExecuteNonQueryAsync("PRAGMA foreign_keys = ON;", cancellationToken);
        await connection.ExecuteNonQueryAsync("PRAGMA busy_timeout = 5000;", cancellationToken);
        return connection;
    }
}
