using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ocr;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    private static void MapDocumentImportEndpoints(WebApplication app)
    {
        app.MapPost("/api/documents/batch", async (
            BatchEnqueueRequest payload,
            IBatchIngestionQueueService batchQueue,
            CancellationToken cancellationToken) =>
        {
            if (payload.FilePaths == null || payload.FilePaths.Count == 0)
            {
                return CreateBadRequestProblem(
                    "Invalid batch request",
                    "Provide a non-empty list of file paths.",
                    "batch_ingestion_empty_list");
            }

            var job = await batchQueue.EnqueueBatchAsync(payload.FilePaths, cancellationToken);
            return Results.Ok(job);
        });

        app.MapGet("/api/documents/batch/{batchId}", async (
            string batchId,
            IBatchIngestionQueueService batchQueue,
            CancellationToken cancellationToken) =>
        {
            var job = await batchQueue.GetBatchStatusAsync(batchId, cancellationToken);
            return job is null ? CreateNotFoundProblem("Batch job") : Results.Ok(job);
        });

        app.MapDelete("/api/documents/batch/{batchId}", async (
            string batchId,
            IBatchIngestionQueueService batchQueue,
            CancellationToken cancellationToken) =>
        {
            await batchQueue.CancelBatchAsync(batchId, cancellationToken);
            return Results.NoContent();
        });

        app.MapGet("/api/documents/batch/{batchId}/stream", async (
            string batchId,
            IBatchIngestionQueueService batchQueue,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var job = await batchQueue.GetBatchStatusAsync(batchId, cancellationToken);
            if (job is null)
            {
                return CreateNotFoundProblem("Batch job");
            }

            httpContext.Response.Headers.Append("Content-Type", "text/event-stream");
            httpContext.Response.Headers.Append("Cache-Control", "no-cache");

            await foreach (var evt in batchQueue.SubscribeProgressAsync(batchId, cancellationToken))
            {
                string json = System.Text.Json.JsonSerializer.Serialize(evt);
                await httpContext.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                await httpContext.Response.Body.FlushAsync(cancellationToken);
            }

            return Results.Empty;
        });

        app.MapPost("/api/documents/import", async (
            HttpRequest request,
            IDocumentLibraryService documents,
            LocalDocumentStorageGuard storageGuard,
            OcrSettingsStore ocrSettings,
            CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType)
            {
                return CreateBadRequestProblem(
                    "Invalid import request",
                    "Use multipart/form-data with one or more files.",
                    "document_import_invalid_content_type");
            }

            if (request.ContentLength > storageGuard.Limits.MaxRequestBodySizeBytes)
            {
                return CreateProblem(
                    "Import too large",
                    $"The request exceeds the limit of {LocalDocumentLibraryLimits.FormatBytes(storageGuard.Limits.MaxRequestBodySizeBytes)}.",
                    StatusCodes.Status413PayloadTooLarge,
                    "document_import_request_too_large");
            }

            IFormCollection form;
            try
            {
                form = await request.ReadFormAsync(cancellationToken);
            }
            catch (BadHttpRequestException)
            {
                return CreateRequestTooLargeProblem();
            }
            catch (InvalidDataException)
            {
                return CreateRequestTooLargeProblem();
            }

            if (form.Files.Count == 0)
            {
                return CreateBadRequestProblem(
                    "No file selected",
                    "Select at least one file to import.",
                    "document_import_files_required");
            }

            try
            {
                ValidateImportBatch(form.Files, storageGuard);
            }
            catch (ArgumentException ex)
            {
                return CreateBadRequestProblem(
                    "Invalid import",
                    ex.Message,
                    "document_import_invalid");
            }
            catch (DocumentStorageLimitException ex)
            {
                return MapDocumentStorageLimitException(ex);
            }

            bool forceOcr = string.Equals(
                form["ocrPolicy"].ToString(),
                "ForceAll",
                StringComparison.OrdinalIgnoreCase);
            string ocrLanguage = await ResolveOcrLanguageAsync(
                form["ocrLanguage"].ToString(),
                ocrSettings,
                cancellationToken);

            List<DocumentImportFileResult> results = [];
            foreach (IFormFile file in form.Files)
            {
                results.Add(await ImportSingleFileAsync(
                    documents,
                    file,
                    forceOcr,
                    ocrLanguage,
                    cancellationToken));
            }

            return Results.Ok(new DocumentImportResponse(
                results.Where(result => result.Succeeded && result.Document is not null)
                    .Select(result => new DocumentImportResult(result.Document!, result.Deduplicated, result.Message))
                    .ToArray(),
                results,
                results.Any(result => !result.Succeeded)));
        });
    }

    public record BatchEnqueueRequest(IReadOnlyList<string> FilePaths);

    private static void ValidateImportBatch(IFormFileCollection files, LocalDocumentStorageGuard storageGuard)
    {
        long totalBytes = 0;
        foreach (IFormFile file in files)
        {
            string safeFileName = SafeDocumentPath.NormalizeFileName(file.FileName);
            storageGuard.EnsureFileWithinLimits(safeFileName, file.Length);
            totalBytes = checked(totalBytes + file.Length);
        }

        storageGuard.EnsureBatchWithinLimits(files.Count, totalBytes);
        storageGuard.EnsureStorageAvailableForBytes(totalBytes);
    }

    private static IResult MapDocumentStorageLimitException(DocumentStorageLimitException exception)
    {
        int statusCode = exception.Kind == DocumentStorageLimitKind.TooManyFiles
            ? StatusCodes.Status400BadRequest
            : StatusCodes.Status413PayloadTooLarge;
        return CreateProblem(
            exception.Title,
            exception.Message,
            statusCode,
            exception.Kind == DocumentStorageLimitKind.TooManyFiles
                ? "document_too_many_files"
                : "document_storage_limit");
    }

    private static IResult CreateRequestTooLargeProblem()
    {
        return CreateProblem(
            "Import too large",
            "The multipart request exceeds the configured limits.",
            StatusCodes.Status413PayloadTooLarge,
            "document_import_request_too_large");
    }

    private static async Task<DocumentImportFileResult> ImportSingleFileAsync(
        IDocumentLibraryService documents,
        IFormFile file,
        bool forceOcr,
        string ocrLanguage,
        CancellationToken cancellationToken)
    {
        string resultFileName = NormalizeSubmittedFileNameForResponse(file.FileName);
        if (file.Length <= 0)
        {
            return DocumentImportFileResult.Failed(
                resultFileName,
                "The file is empty.",
                "document_import_empty_file");
        }

        try
        {
            await using Stream stream = file.OpenReadStream();
            DocumentImportResult imported = await documents.ImportAsync(
                stream,
                file.FileName,
                forceOcr,
                ocrLanguage,
                cancellationToken);
            return DocumentImportFileResult.Imported(resultFileName, imported);
        }
        catch (ArgumentException ex)
        {
            return DocumentImportFileResult.Failed(resultFileName, ex.Message, "document_import_invalid");
        }
        catch (DocumentStorageLimitException ex)
        {
            return DocumentImportFileResult.Failed(resultFileName, ex.Message, "document_storage_limit");
        }
        catch (InvalidOperationException)
        {
            return DocumentImportFileResult.Failed(
                resultFileName,
                "The file cannot be saved in the local library.",
                "document_import_invalid_path");
        }
    }

    private static string NormalizeSubmittedFileNameForResponse(string fileName)
    {
        try
        {
            return SafeDocumentPath.NormalizeFileName(fileName);
        }
        catch (ArgumentException)
        {
            return "file";
        }
    }
}
