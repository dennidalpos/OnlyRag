using Microsoft.Data.Sqlite;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Storage;

public sealed partial class SqliteDocumentRepository
{
    public async Task<IReadOnlyList<ImportedDocument>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT d.id, d.document_uid, d.original_file_name, d.original_path, d.sha256, d.mime_type,
                   d.file_extension, d.file_size_bytes, d.status, d.page_count,
                   COALESCE(c.chunk_count, 0) AS chunk_count,
                   d.current_job_id, d.last_error, d.created_at_utc, d.updated_at_utc
            FROM documents
            AS d
            LEFT JOIN (
                SELECT document_id, COUNT(*) AS chunk_count
                FROM chunks
                GROUP BY document_id
            ) AS c ON c.document_id = d.id
            ORDER BY d.created_at_utc DESC, d.id DESC;
            """;

        List<ImportedDocument> documents = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            documents.Add(ReadDocument(reader));
        }

        return documents;
    }

    public async Task<ImportedDocument?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT d.id, d.document_uid, d.original_file_name, d.original_path, d.sha256, d.mime_type,
                   d.file_extension, d.file_size_bytes, d.status, d.page_count,
                   COALESCE(c.chunk_count, 0) AS chunk_count,
                   d.current_job_id, d.last_error, d.created_at_utc, d.updated_at_utc
            FROM documents
            AS d
            LEFT JOIN (
                SELECT document_id, COUNT(*) AS chunk_count
                FROM chunks
                GROUP BY document_id
            ) AS c ON c.document_id = d.id
            WHERE d.id = $id;
            """;
        command.AddParameter("$id", id);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadDocument(reader) : null;
    }

    public async Task<ImportedDocument?> FindBySha256Async(string sha256, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT d.id, d.document_uid, d.original_file_name, d.original_path, d.sha256, d.mime_type,
                   d.file_extension, d.file_size_bytes, d.status, d.page_count,
                   COALESCE(c.chunk_count, 0) AS chunk_count,
                   d.current_job_id, d.last_error, d.created_at_utc, d.updated_at_utc
            FROM documents
            AS d
            LEFT JOIN (
                SELECT document_id, COUNT(*) AS chunk_count
                FROM chunks
                GROUP BY document_id
            ) AS c ON c.document_id = d.id
            WHERE d.sha256 = $sha256
            ORDER BY d.created_at_utc ASC
            LIMIT 1;
            """;
        command.AddParameter("$sha256", sha256);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadDocument(reader) : null;
    }

    private static ImportedDocument ReadDocument(SqliteDataReader reader)
    {
        return new ImportedDocument(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetInt64(7),
            Enum.Parse<DocumentStatus>(reader.GetString(8), ignoreCase: true),
            reader.GetInt32(9),
            reader.GetInt32(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            DateTimeOffset.Parse(reader.GetString(13)),
            DateTimeOffset.Parse(reader.GetString(14)));
    }

}