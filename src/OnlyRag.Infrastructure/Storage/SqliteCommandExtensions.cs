using Microsoft.Data.Sqlite;

namespace OnlyRag.Infrastructure.Storage;

internal static class SqliteCommandExtensions
{
    public static async Task<int> ExecuteNonQueryAsync(
        this SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken = default)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static SqliteParameter AddParameter(
        this SqliteCommand command,
        string name,
        object? value)
    {
        SqliteParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
        return parameter;
    }
}
