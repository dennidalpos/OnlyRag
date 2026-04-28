using Microsoft.Data.Sqlite;

namespace OnlyRag.Infrastructure.Storage;

public interface ISqliteConnectionFactory
{
    Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}
