using Microsoft.Data.Sqlite;

namespace OnlyRag.Infrastructure.Storage;

public sealed class SqliteSettingsRepository : ISettingsRepository
{
    private readonly ISqliteConnectionFactory connectionFactory;

    public SqliteSettingsRepository(ISqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = $key;";
        command.AddParameter("$key", key);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value as string;
    }

    public async Task UpsertAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO settings(key, value, value_type, updated_at_utc)
            VALUES ($key, $value, 'string', strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value,
                value_type = excluded.value_type,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.AddParameter("$key", key);
        command.AddParameter("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
