using Microsoft.Data.Sqlite;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Storage;

public sealed class SqliteArchiveManifestRepository : IArchiveManifestRepository
{
    private readonly ISqliteConnectionFactory connectionFactory;

    public SqliteArchiveManifestRepository(ISqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<ArchiveManifestEntry?> GetAsync(
        long containerDocumentId,
        int entryIndex,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = SelectSql + " WHERE container_document_id = $documentId AND entry_index = $entryIndex;";
        command.AddParameter("$documentId", containerDocumentId);
        command.AddParameter("$entryIndex", entryIndex);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadEntry(reader) : null;
    }

    public async Task<ArchiveManifestEntry?> FindByPathAsync(
        long containerDocumentId,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = SelectSql + " WHERE container_document_id = $documentId AND relative_path = $path ORDER BY entry_index ASC LIMIT 1;";
        AddDocumentAndPathParameters(command, containerDocumentId, relativePath);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadEntry(reader) : null;
    }

    public async Task<ArchiveManifestEntry> UpsertPendingAsync(
        long containerDocumentId,
        int entryIndex,
        string relativePath,
        long declaredSizeBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        string now = DateTimeOffset.UtcNow.ToString("O");

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        ArchiveManifestEntry? existing = await GetAsync(containerDocumentId, entryIndex, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT OR IGNORE INTO archive_manifest_entries (container_document_id, entry_index, relative_path, declared_size_bytes, status, created_at_utc, updated_at_utc) " +
            "VALUES ($documentId, $entryIndex, $path, $declaredSizeBytes, $pendingStatus, $now, $now);";
        AddDocumentAndPathParameters(command, containerDocumentId, relativePath);
        command.AddParameter("$entryIndex", entryIndex);
        command.AddParameter("$declaredSizeBytes", declaredSizeBytes);
        command.AddParameter("$pendingStatus", ArchiveManifestStatus.Pending.ToString());
        command.AddParameter("$now", now);

        await command.ExecuteNonQueryAsync(cancellationToken);
        ArchiveManifestEntry? created = await GetAsync(containerDocumentId, entryIndex, cancellationToken);
        if (created is null)
        {
            throw new InvalidOperationException("Archive manifest entry could not be created.");
        }

        return created;
    }

    public async Task<ArchiveManifestEntry?> UpdateAsync(
        long containerDocumentId,
        int entryIndex,
        ArchiveManifestStatus status,
        long? uncompressedSizeBytes = null,
        string? contentSha256 = null,
        string? error = null,
        int? pageCount = null,
        int? chunkCount = null,
        CancellationToken cancellationToken = default)
    {
        string now = DateTimeOffset.UtcNow.ToString("O");
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "UPDATE archive_manifest_entries SET status = $status, uncompressed_size_bytes = COALESCE($uncompressedSizeBytes, uncompressed_size_bytes), content_sha256 = COALESCE($contentSha256, content_sha256), error = $error, page_count = COALESCE($pageCount, page_count), chunk_count = COALESCE($chunkCount, chunk_count), updated_at_utc = $now WHERE container_document_id = $documentId AND entry_index = $entryIndex;";
        command.AddParameter("$documentId", containerDocumentId);
        command.AddParameter("$entryIndex", entryIndex);
        command.AddParameter("$status", status.ToString());
        command.AddParameter("$uncompressedSizeBytes", (object?)uncompressedSizeBytes ?? DBNull.Value);
        command.AddParameter("$contentSha256", (object?)contentSha256 ?? DBNull.Value);
        command.AddParameter("$error", (object?)error ?? DBNull.Value);
        command.AddParameter("$pageCount", (object?)pageCount ?? DBNull.Value);
        command.AddParameter("$chunkCount", (object?)chunkCount ?? DBNull.Value);
        command.AddParameter("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return await GetAsync(containerDocumentId, entryIndex, cancellationToken);
    }

    public async Task<IReadOnlyList<ArchiveManifestEntry>> ListAsync(
        long containerDocumentId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = SelectSql + " WHERE container_document_id = $documentId ORDER BY entry_index ASC, id ASC;";
        command.AddParameter("$documentId", containerDocumentId);

        List<ArchiveManifestEntry> entries = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(ReadEntry(reader));
        }

        return entries;
    }

    private const string SelectSql =
        "SELECT id, container_document_id, entry_index, relative_path, declared_size_bytes, uncompressed_size_bytes, content_sha256, status, error, page_count, chunk_count, created_at_utc, updated_at_utc FROM archive_manifest_entries";

    private static void AddDocumentAndPathParameters(SqliteCommand command, long documentId, string path)
    {
        command.AddParameter("$documentId", documentId);
        command.AddParameter("$path", path);
    }

    private static ArchiveManifestEntry ReadEntry(SqliteDataReader reader)
    {
        return new ArchiveManifestEntry(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt32(2),
            reader.GetString(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            Enum.Parse<ArchiveManifestStatus>(reader.GetString(7), ignoreCase: true),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.GetInt32(9),
            reader.GetInt32(10),
            DateTimeOffset.Parse(reader.GetString(11)),
            DateTimeOffset.Parse(reader.GetString(12)));
    }
}
