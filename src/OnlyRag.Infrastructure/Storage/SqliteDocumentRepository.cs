using Microsoft.Data.Sqlite;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Storage;

public sealed partial class SqliteDocumentRepository : IDocumentRepository
{
    private readonly ISqliteConnectionFactory connectionFactory;

    public SqliteDocumentRepository(ISqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }


    public async Task<int> CountByOriginalPathAsync(string originalPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM documents WHERE original_path = $originalPath;";
        command.AddParameter("$originalPath", originalPath);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<ImportedDocument> CreateAsync(
        CreateDocumentRecordRequest request,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO documents (
                document_uid,
                original_file_name,
                original_path,
                sha256,
                mime_type,
                file_extension,
                file_size_bytes,
                status,
                page_count,
                current_job_id,
                last_error,
                created_at_utc,
                updated_at_utc
            )
            VALUES (
                $documentUid,
                $originalFileName,
                $originalPath,
                $sha256,
                $mimeType,
                $fileExtension,
                $fileSizeBytes,
                $status,
                $pageCount,
                $currentJobId,
                $lastError,
                $createdAtUtc,
                $updatedAtUtc
            );
            SELECT last_insert_rowid();
            """;
        command.AddParameter("$documentUid", request.DocumentUid);
        command.AddParameter("$originalFileName", request.OriginalFileName);
        command.AddParameter("$originalPath", request.OriginalPath);
        command.AddParameter("$sha256", request.Sha256);
        command.AddParameter("$mimeType", request.MimeType);
        command.AddParameter("$fileExtension", request.FileExtension);
        command.AddParameter("$fileSizeBytes", request.FileSizeBytes);
        command.AddParameter("$status", request.Status.ToString());
        command.AddParameter("$pageCount", request.PageCount);
        command.AddParameter("$currentJobId", request.CurrentJobId);
        command.AddParameter("$lastError", request.LastError);
        command.AddParameter("$createdAtUtc", request.CreatedAtUtc.ToString("O"));
        command.AddParameter("$updatedAtUtc", request.UpdatedAtUtc.ToString("O"));

        long id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        return (await GetAsync(id, cancellationToken))!;
    }

    public async Task<ImportedDocument?> UpdateStatusAsync(
        long id,
        DocumentStatus status,
        string? currentJobId,
        string? lastError,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE documents
            SET status = $status,
                current_job_id = $currentJobId,
                last_error = $lastError,
                updated_at_utc = $updatedAtUtc
            WHERE id = $id;
            """;
        command.AddParameter("$id", id);
        command.AddParameter("$status", status.ToString());
        command.AddParameter("$currentJobId", currentJobId);
        command.AddParameter("$lastError", lastError);
        command.AddParameter("$updatedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

public async Task<ImportedDocument?> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        ImportedDocument? current = await GetAsync(id, cancellationToken);
        if (current is null)
        {
            return null;
        }

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        // Delete child rows explicitly in dependency order to avoid SQLite erroring when the same
        // rows (chunks) are subject to both ON DELETE CASCADE (from documents) and ON DELETE SET NULL
        // (from document_pages) in the same cascade operation.
        foreach (string sql in new[]
        {
            "DELETE FROM chunk_vector_index_status WHERE chunk_id IN (SELECT id FROM chunks WHERE document_id = $id);",
            "DELETE FROM chunks WHERE document_id = $id;",
            "DELETE FROM translation_units WHERE translation_id IN (SELECT id FROM translations WHERE document_id = $id);",
            "DELETE FROM translations WHERE document_id = $id;",
            "DELETE FROM document_pages WHERE document_id = $id;",
            "DELETE FROM documents WHERE id = $id;"
        })
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.AddParameter("$id", id);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return current;
    }

}
