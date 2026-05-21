using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OnlyRag.Core;
using OnlyRag.Worker;

namespace OnlyRag.Infrastructure.Storage;

public sealed class LocalDocumentLibraryService : IDocumentLibraryService
{
    public const string DocumentIngestionJobType = "document-ingestion";

    private readonly LocalSqliteStoreDescriptor descriptor;
    private readonly IDocumentRepository documents;
    private readonly ILocalJobQueue jobQueue;
    private readonly LocalDocumentStorageGuard storageGuard;

    public LocalDocumentLibraryService(
        LocalSqliteStoreDescriptor descriptor,
        IDocumentRepository documents,
        ILocalJobQueue jobQueue,
        LocalDocumentStorageGuard storageGuard)
    {
        this.descriptor = descriptor;
        this.documents = documents;
        this.jobQueue = jobQueue;
        this.storageGuard = storageGuard;
    }

    public Task<IReadOnlyList<ImportedDocument>> ListAsync(CancellationToken cancellationToken = default)
    {
        return documents.ListAsync(cancellationToken);
    }

    public Task<ImportedDocument?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        return documents.GetAsync(id, cancellationToken);
    }

    public async Task<DocumentImportResult> ImportAsync(
        Stream source,
        string fileName,
        bool forceOcr = false,
        string? ocrLanguage = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        string safeFileName = SafeDocumentPath.NormalizeFileName(fileName);
        string fileExtension = SafeDocumentPath.NormalizeFileExtension(safeFileName);
        string mimeType = DocumentFileTypeDetector.DetectMimeType(safeFileName);

        if (!IsIngestionSupported(fileExtension))
        {
            throw new ArgumentException(
                $"Il formato '{fileExtension}' non e supportato. Formati accettati: TXT, MD, MARKDOWN, CSV, PDF, PNG, JPG, JPEG, BMP, GIF, TIFF, WEBP, DOCX, XLSX, PPTX, DOC, XLS, PPT.",
                nameof(fileName));
        }

        long expectedBytes = source.CanSeek ? Math.Max(0, source.Length - source.Position) : 0;
        if (expectedBytes > 0)
        {
            storageGuard.EnsureFileWithinLimits(safeFileName, expectedBytes);
            storageGuard.EnsureStorageAvailableForBytes(expectedBytes);
        }

        Directory.CreateDirectory(descriptor.Paths.DocumentOriginalsDirectory);

        string temporaryPath = SafeDocumentPath.ResolveWithinRoot(
            descriptor.Paths.DocumentOriginalsDirectory,
            $"{Guid.NewGuid():N}.upload");

        (string sha256, long fileSizeBytes) = await CopyToTemporaryFileAndHashAsync(
            source,
            temporaryPath,
            storageGuard.Limits.MaxFileSizeBytes,
            cancellationToken);
        storageGuard.EnsureStorageAvailableForBytes(fileSizeBytes);

        ImportedDocument? existing = await documents.FindBySha256Async(sha256, cancellationToken);
        if (existing is not null)
        {
            File.Delete(temporaryPath);
            return new DocumentImportResult(
                existing,
                Deduplicated: true,
                "Documento gia presente. Import annullato senza copiare un duplicato.");
        }

        string finalPath = SafeDocumentPath.ResolveWithinRoot(
            descriptor.Paths.DocumentOriginalsDirectory,
            $"{sha256}{fileExtension}");

        MoveIntoOriginals(temporaryPath, finalPath);

        string documentUid = Guid.NewGuid().ToString("N");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ImportedDocument created;
        try
        {
            created = await documents.CreateAsync(
                new CreateDocumentRecordRequest(
                    documentUid,
                    safeFileName,
                    finalPath,
                    sha256,
                    mimeType,
                    fileExtension,
                    fileSizeBytes,
                    DocumentStatus.Imported,
                    PageCount: 0,
                    CurrentJobId: null,
                    LastError: null,
                    now,
                    now),
                cancellationToken);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            ImportedDocument? duplicate = await documents.FindBySha256Async(sha256, cancellationToken);
            if (duplicate is null)
            {
                throw;
            }

            await DeleteOriginalIfUnreferencedAsync(finalPath, cancellationToken);
            return new DocumentImportResult(
                duplicate,
                Deduplicated: true,
                "Documento gia presente. Import annullato senza copiare un duplicato.");
        }

        try
        {
            ImportedDocument queued = await QueueDocumentAsync(created, forceOcr, ocrLanguage, cancellationToken);
            return new DocumentImportResult(queued, Deduplicated: false, "Documento importato e messo in coda.");
        }
        catch
        {
            await documents.DeleteAsync(created.Id, cancellationToken);
            await DeleteOriginalIfUnreferencedAsync(finalPath, cancellationToken);

            throw;
        }
    }

    public Task<ImportedDocument?> QueueForIndexingAsync(
        long id,
        string? ocrLanguage = null,
        CancellationToken cancellationToken = default)
    {
        return QueueDocumentByIdAsync(id, forceOcr: false, ocrLanguage, cancellationToken);
    }

    public Task<ImportedDocument?> SetStatusAsync(
        long id,
        DocumentStatus status,
        string? currentJobId,
        string? lastError,
        CancellationToken cancellationToken = default)
    {
        return documents.UpdateStatusAsync(id, status, currentJobId, lastError, cancellationToken);
    }

    public async Task<ImportedDocument?> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        ImportedDocument? document = await documents.DeleteAsync(id, cancellationToken);
        if (document is null)
        {
            return null;
        }

        await DeleteOriginalIfUnreferencedAsync(document.OriginalPath, cancellationToken);

        return document;
    }

    private async Task DeleteOriginalIfUnreferencedAsync(
        string originalPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(originalPath))
        {
            return;
        }

        if (await documents.CountByOriginalPathAsync(originalPath, cancellationToken) > 0)
        {
            return;
        }

        try
        {
            File.Delete(originalPath);
        }
        catch (IOException)
        {
            // File locked by a running ingestion job; the record is already removed from SQLite.
            // The orphaned file will remain on disk and can be cleaned up manually.
        }
        catch (UnauthorizedAccessException)
        {
            // No delete permission; leave the file on disk.
        }
    }

    private async Task<ImportedDocument?> QueueDocumentByIdAsync(
        long id,
        bool forceOcr,
        string? ocrLanguage,
        CancellationToken cancellationToken)
    {
        ImportedDocument? document = await documents.GetAsync(id, cancellationToken);
        if (document is null)
        {
            return null;
        }

        return await QueueDocumentAsync(document, forceOcr, ocrLanguage, cancellationToken);
    }

    private async Task<ImportedDocument> QueueDocumentAsync(
        ImportedDocument document,
        bool forceOcr,
        string? ocrLanguage,
        CancellationToken cancellationToken)
    {
        string payloadJson = JsonSerializer.Serialize(new DocumentIngestionJobPayload(
            document.Id,
            document.DocumentUid,
            document.OriginalFileName,
            document.Sha256 ?? string.Empty,
            ForceOcr: forceOcr,
            OcrLanguage: OcrLanguages.NormalizeCode(ocrLanguage)));

        LocalJob job = await jobQueue.CreateAsync(
            new CreateLocalJobRequest(
                DocumentIngestionJobType,
                payloadJson,
                Priority: 20,
                MaxRetries: 0),
            cancellationToken);

        return (await documents.UpdateStatusAsync(
            document.Id,
            DocumentStatus.Queued,
            job.Id,
            lastError: null,
            cancellationToken))!;
    }

    private static bool IsIngestionSupported(string normalizedExtension)
    {
        return normalizedExtension is
            ".txt" or ".md" or ".markdown" or ".csv" or
            ".pdf" or
            ".png" or ".jpg" or ".jpeg" or ".tif" or ".tiff" or ".bmp" or ".gif" or ".webp" or
            ".docx" or ".xlsx" or ".pptx" or
            ".doc" or ".xls" or ".ppt";
    }

    private static void MoveIntoOriginals(string temporaryPath, string finalPath)
    {
        if (File.Exists(finalPath))
        {
            File.Delete(temporaryPath);
            return;
        }

        try
        {
            File.Move(temporaryPath, finalPath);
        }
        catch (IOException) when (File.Exists(finalPath))
        {
            File.Delete(temporaryPath);
        }
    }

    private static async Task<(string Sha256, long FileSizeBytes)> CopyToTemporaryFileAndHashAsync(
        Stream source,
        string temporaryPath,
        long maxFileSizeBytes,
        CancellationToken cancellationToken)
    {
        byte[] buffer = GC.AllocateUninitializedArray<byte>(1024 * 64);
        long fileSizeBytes = 0;
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            await using FileStream destination = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                buffer.Length,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            while (true)
            {
                int read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                fileSizeBytes += read;
                if (fileSizeBytes > maxFileSizeBytes)
                {
                    throw new DocumentStorageLimitException(
                        DocumentStorageLimitKind.FileTooLarge,
                        "File troppo grande",
                        "Il file supera il limite configurato per singolo documento.");
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                hash.AppendData(buffer, 0, read);
            }

            await destination.FlushAsync(cancellationToken);
            string sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            return (sha256, fileSizeBytes);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }
}
