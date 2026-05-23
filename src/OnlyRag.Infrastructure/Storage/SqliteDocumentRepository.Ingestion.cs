using Microsoft.Data.Sqlite;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Storage;

public sealed partial class SqliteDocumentRepository
{
    public async Task ClearIngestionAsync(long documentId, CancellationToken cancellationToken = default)
    {
        string now = DateTimeOffset.UtcNow.ToString("O");

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (SqliteCommand deleteChunks = connection.CreateCommand())
        {
            deleteChunks.Transaction = transaction;
            deleteChunks.CommandText = "DELETE FROM chunks WHERE document_id = $documentId;";
            deleteChunks.AddParameter("$documentId", documentId);
            await deleteChunks.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (SqliteCommand deletePages = connection.CreateCommand())
        {
            deletePages.Transaction = transaction;
            deletePages.CommandText = "DELETE FROM document_pages WHERE document_id = $documentId;";
            deletePages.AddParameter("$documentId", documentId);
            await deletePages.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (SqliteCommand updateDocument = connection.CreateCommand())
        {
            updateDocument.Transaction = transaction;
            updateDocument.CommandText =
                """
                UPDATE documents
                SET page_count = 0,
                    updated_at_utc = $now
                WHERE id = $documentId;
                """;
            updateDocument.AddParameter("$documentId", documentId);
            updateDocument.AddParameter("$now", now);
            await updateDocument.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveIngestedPageAsync(
        long documentId,
        IngestedDocumentPage page,
        IReadOnlyList<IngestedDocumentChunk> chunks,
        int pageCount,
        CancellationToken cancellationToken = default)
    {
        string now = DateTimeOffset.UtcNow.ToString("O");

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        long pageId;

        await using (SqliteCommand upsertPage = connection.CreateCommand())
        {
            upsertPage.Transaction = transaction;
            upsertPage.CommandText =
                """
                INSERT INTO document_pages (
                    document_id,
                    page_number,
                    render_path,
                    ocr_cache_path,
                    text_content,
                    ocr_status,
                    ocr_engine,
                    ocr_language,
                    ocr_confidence,
                    ocr_boxes_json,
                    ocr_error,
                    ocr_completed_at_utc,
                    created_at_utc,
                    updated_at_utc
                )
                VALUES (
                    $documentId,
                    $pageNumber,
                    $renderPath,
                    $ocrCacheKey,
                    $textContent,
                    $ocrStatus,
                    $ocrEngine,
                    $ocrLanguage,
                    $ocrConfidence,
                    $ocrBoxesJson,
                    $ocrError,
                    CASE WHEN $ocrStatus IN ('Complete', 'Cached', 'LowConfidence') THEN $now ELSE NULL END,
                    $now,
                    $now
                )
                ON CONFLICT(document_id, page_number) DO UPDATE SET
                    render_path = excluded.render_path,
                    ocr_cache_path = excluded.ocr_cache_path,
                    text_content = excluded.text_content,
                    ocr_status = excluded.ocr_status,
                    ocr_engine = excluded.ocr_engine,
                    ocr_language = excluded.ocr_language,
                    ocr_confidence = excluded.ocr_confidence,
                    ocr_boxes_json = excluded.ocr_boxes_json,
                    ocr_error = excluded.ocr_error,
                    ocr_completed_at_utc = excluded.ocr_completed_at_utc,
                    updated_at_utc = excluded.updated_at_utc
                RETURNING id;
                """;
            upsertPage.AddParameter("$documentId", documentId);
            upsertPage.AddParameter("$pageNumber", page.PageNumber);
            upsertPage.AddParameter("$renderPath", page.RenderPath);
            upsertPage.AddParameter("$ocrCacheKey", page.OcrCacheKey);
            upsertPage.AddParameter("$textContent", page.Text);
            upsertPage.AddParameter("$ocrStatus", page.OcrStatus);
            upsertPage.AddParameter("$ocrEngine", page.OcrEngine);
            upsertPage.AddParameter("$ocrLanguage", page.OcrLanguage);
            upsertPage.AddParameter("$ocrConfidence", page.OcrConfidence);
            upsertPage.AddParameter("$ocrBoxesJson", page.OcrBoxesJson);
            upsertPage.AddParameter("$ocrError", page.OcrError);
            upsertPage.AddParameter("$now", now);
            pageId = Convert.ToInt64(await upsertPage.ExecuteScalarAsync(cancellationToken));
        }

        foreach (IngestedDocumentChunk chunk in chunks)
        {
            await using SqliteCommand insertChunk = connection.CreateCommand();
            insertChunk.Transaction = transaction;
            insertChunk.CommandText =
                """
                INSERT INTO chunks (
                    document_id,
                    document_page_id,
                    chunk_index,
                    content,
                    token_count,
                    page_start,
                    page_end,
                    content_hash,
                    metadata_json,
                    created_at_utc,
                    updated_at_utc
                )
                VALUES (
                    $documentId,
                    $pageId,
                    $ordinal,
                    $text,
                    $tokenCount,
                    $pageStart,
                    $pageEnd,
                    $contentHash,
                    $metadataJson,
                    $now,
                    $now
                )
                ON CONFLICT(document_id, chunk_index) DO UPDATE SET
                    document_page_id = excluded.document_page_id,
                    content = excluded.content,
                    token_count = excluded.token_count,
                    page_start = excluded.page_start,
                    page_end = excluded.page_end,
                    content_hash = excluded.content_hash,
                    metadata_json = excluded.metadata_json,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            insertChunk.AddParameter("$documentId", documentId);
            insertChunk.AddParameter("$pageId", pageId);
            insertChunk.AddParameter("$ordinal", chunk.Ordinal);
            insertChunk.AddParameter("$text", chunk.Text);
            insertChunk.AddParameter("$tokenCount", chunk.ApproximateTokenCount);
            insertChunk.AddParameter("$pageStart", chunk.PageStart);
            insertChunk.AddParameter("$pageEnd", chunk.PageEnd);
            insertChunk.AddParameter("$contentHash", chunk.ContentHash);
            insertChunk.AddParameter(
                "$metadataJson",
                $$"""{"document_id":{{documentId}},"page_start":{{chunk.PageStart}},"page_end":{{chunk.PageEnd}},"ordinal":{{chunk.Ordinal}},"token_count":{{chunk.ApproximateTokenCount}},"content_hash":"{{chunk.ContentHash}}"}""");
            insertChunk.AddParameter("$now", now);
            await insertChunk.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (SqliteCommand updateDocument = connection.CreateCommand())
        {
            updateDocument.Transaction = transaction;
            updateDocument.CommandText =
                """
                UPDATE documents
                SET page_count = $pageCount,
                    updated_at_utc = $now
                WHERE id = $documentId;
                """;
            updateDocument.AddParameter("$documentId", documentId);
            updateDocument.AddParameter("$pageCount", pageCount);
            updateDocument.AddParameter("$now", now);
            await updateDocument.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<DocumentOcrStatusResponse> GetOcrStatusAsync(
        long documentId,
        string? currentJobId,
        string? currentStep,
        CancellationToken cancellationToken = default)
    {
        ImportedDocument? document = await GetAsync(documentId, cancellationToken);
        if (document is null)
        {
            throw new InvalidOperationException("Documento non trovato.");
        }

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*),
                   COALESCE(SUM(CASE WHEN ocr_status IN ('Complete', 'Cached', 'LowConfidence') THEN 1 ELSE 0 END), 0),
                   AVG(ocr_confidence),
                   MAX(ocr_engine),
                   MAX(CASE WHEN ocr_error IS NOT NULL AND ocr_error <> '' THEN ocr_error ELSE NULL END)
            FROM document_pages
            WHERE document_id = $documentId;
            """;
        command.AddParameter("$documentId", documentId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        int pageRows = reader.GetInt32(0);
        int ocrPageCount = reader.GetInt32(1);
        double? averageConfidence = reader.IsDBNull(2) ? null : reader.GetDouble(2);
        string? engineName = reader.IsDBNull(3) ? null : reader.GetString(3);
        string? lastError = reader.IsDBNull(4) ? document.LastError : reader.GetString(4);

        string state;
        if (!string.IsNullOrWhiteSpace(currentJobId))
        {
            state = "Running";
        }
        else if (ocrPageCount == 0)
        {
            state = document.Status is DocumentStatus.Failed ? "Failed" : "NotStarted";
        }
        else if (document.PageCount > 0 && ocrPageCount >= document.PageCount)
        {
            state = "Complete";
        }
        else
        {
            state = "Partial";
        }

        return new DocumentOcrStatusResponse(
            documentId,
            state,
            document.PageCount,
            ocrPageCount,
            ocrPageCount,
            document.PageCount > 0 ? document.PageCount : Math.Max(document.PageCount, pageRows),
            averageConfidence,
            currentJobId,
            currentStep,
            engineName,
            lastError);
    }

    public async Task<IReadOnlyList<DocumentPageInfo>> GetPagesAsync(
        long documentId,
        int pageStart,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageStart);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT page_number, text_content, ocr_status, ocr_engine, ocr_confidence, ocr_error
            FROM document_pages
            WHERE document_id = $documentId
              AND page_number >= $pageStart
            ORDER BY page_number ASC
            LIMIT $pageSize;
            """;
        command.AddParameter("$documentId", documentId);
        command.AddParameter("$pageStart", pageStart);
        command.AddParameter("$pageSize", pageSize);

        List<DocumentPageInfo> pages = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            pages.Add(new DocumentPageInfo(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetDouble(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return pages;
    }
}
