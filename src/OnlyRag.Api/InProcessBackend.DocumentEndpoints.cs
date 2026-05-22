using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    private const int DefaultDocumentPreviewPageSize = 1;
    private const int MaxDocumentPreviewPageSize = 10;

    private static void MapDocumentEndpoints(WebApplication app)
    {
        app.MapGet("/api/documents", async (IDocumentLibraryService documents, CancellationToken cancellationToken) =>
            Results.Ok(await documents.ListAsync(cancellationToken)));

        app.MapGet("/api/documents/{id:long}", async (
            long id,
            IDocumentLibraryService documents,
            CancellationToken cancellationToken) =>
        {
            ImportedDocument? document = await documents.GetAsync(id, cancellationToken);
            return document is null ? CreateNotFoundProblem("Documento") : Results.Ok(document);
        });

        app.MapPost("/api/documents/import", async (
            HttpRequest request,
            IDocumentLibraryService documents,
            LocalDocumentStorageGuard storageGuard,
            CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType)
            {
                return CreateBadRequestProblem(
                    "Richiesta import non valida",
                    "Usa multipart/form-data con uno o piu file.",
                    "document_import_invalid_content_type");
            }

            if (request.ContentLength > storageGuard.Limits.MaxRequestBodySizeBytes)
            {
                return CreateProblem(
                    "Import troppo grande",
                    $"La richiesta supera il limite di {LocalDocumentLibraryLimits.FormatBytes(storageGuard.Limits.MaxRequestBodySizeBytes)}.",
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
                return CreateProblem(
                    "Import troppo grande",
                    "La richiesta multipart supera i limiti configurati.",
                    StatusCodes.Status413PayloadTooLarge,
                    "document_import_request_too_large");
            }
            catch (InvalidDataException)
            {
                return CreateProblem(
                    "Import troppo grande",
                    "La richiesta multipart supera i limiti configurati.",
                    StatusCodes.Status413PayloadTooLarge,
                    "document_import_request_too_large");
            }

            if (form.Files.Count == 0)
            {
                return CreateBadRequestProblem(
                    "Nessun file selezionato",
                    "Seleziona almeno un file da importare.",
                    "document_import_files_required");
            }

            try
            {
                ValidateImportBatch(form.Files, storageGuard);
            }
            catch (DocumentStorageLimitException ex)
            {
                return MapDocumentStorageLimitException(ex);
            }

            bool forceOcr = string.Equals(
                form["ocrPolicy"].ToString(),
                "ForceAll",
                StringComparison.OrdinalIgnoreCase);
            string ocrLanguage = OcrLanguages.NormalizeCode(form["ocrLanguage"].ToString());

            List<DocumentImportResult> importedDocuments = [];
            foreach (IFormFile file in form.Files)
            {
                if (file.Length <= 0)
                {
                    return CreateBadRequestProblem(
                        "File vuoto",
                        $"Il file '{file.FileName}' e vuoto.",
                        "document_import_empty_file");
                }

                try
                {
                    await using Stream stream = file.OpenReadStream();
                    importedDocuments.Add(await documents.ImportAsync(stream, file.FileName, forceOcr, ocrLanguage, cancellationToken));
                }
                catch (ArgumentException ex)
                {
                    return CreateBadRequestProblem(
                        "Import non valido",
                        ex.Message,
                        "document_import_invalid");
                }
                catch (DocumentStorageLimitException ex)
                {
                    return MapDocumentStorageLimitException(ex);
                }
                catch (InvalidOperationException ex)
                {
                    return CreateBadRequestProblem(
                        "Path documento non valido",
                        ex.Message,
                        "document_import_invalid_path");
                }
            }

            return Results.Ok(new DocumentImportResponse(importedDocuments));
        });

        app.MapDelete("/api/documents/{id:long}", async (
            long id,
            IDocumentLibraryService documents,
            ILocalJobQueue jobs,
            RunningJobCancellationRegistry cancellationRegistry,
            CancellationToken cancellationToken) =>
        {
            ImportedDocument? existing = await documents.GetAsync(id, cancellationToken);
            if (existing is null)
            {
                return CreateNotFoundProblem("Documento");
            }

            await CancelDocumentJobIfNeededAsync(existing, jobs, cancellationRegistry, cancellationToken);
            ImportedDocument? deleted = await documents.DeleteAsync(id, cancellationToken);
            return deleted is null ? CreateNotFoundProblem("Documento") : Results.Ok(deleted);
        });

        app.MapPost("/api/documents/{id:long}/reindex", async (
            long id,
            string? ocrLanguage,
            IDocumentLibraryService documents,
            ILocalJobQueue jobs,
            RunningJobCancellationRegistry cancellationRegistry,
            CancellationToken cancellationToken) =>
        {
            ImportedDocument? existing = await documents.GetAsync(id, cancellationToken);
            if (existing is null)
            {
                return CreateNotFoundProblem("Documento");
            }

            await CancelDocumentJobIfNeededAsync(existing, jobs, cancellationRegistry, cancellationToken);
            ImportedDocument? queued = await documents.QueueForIndexingAsync(id, OcrLanguages.NormalizeCode(ocrLanguage), cancellationToken);
            return queued is null ? CreateNotFoundProblem("Documento") : Results.Ok(queued);
        });

        app.MapGet("/api/documents/{id:long}/ocr-status", async (
            long id,
            IDocumentLibraryService documents,
            IDocumentRepository documentRepository,
            ILocalJobQueue jobs,
            CancellationToken cancellationToken) =>
        {
            ImportedDocument? document = await documents.GetAsync(id, cancellationToken);
            if (document is null)
            {
                return CreateNotFoundProblem("Documento");
            }

            LocalJob? currentJob = await GetActiveDocumentJobAsync(document, jobs, cancellationToken);
            return Results.Ok(await documentRepository.GetOcrStatusAsync(
                id,
                currentJob?.Id,
                currentJob?.CurrentStep,
                cancellationToken));
        });

        app.MapPost("/api/documents/{id:long}/ocr", async (
            long id,
            bool? force,
            string? ocrLanguage,
            IDocumentLibraryService documents,
            ILocalJobQueue jobs,
            RunningJobCancellationRegistry cancellationRegistry,
            CancellationToken cancellationToken) =>
        {
            ImportedDocument? document = await documents.GetAsync(id, cancellationToken);
            if (document is null)
            {
                return CreateNotFoundProblem("Documento");
            }

            await CancelDocumentJobIfNeededAsync(document, jobs, cancellationRegistry, cancellationToken);
            string payloadJson = System.Text.Json.JsonSerializer.Serialize(new DocumentIngestionJobPayload(
                document.Id,
                document.DocumentUid,
                document.OriginalFileName,
                document.Sha256 ?? string.Empty,
                ForceOcr: force ?? false,
                OcrLanguage: OcrLanguages.NormalizeCode(ocrLanguage)));
            LocalJob job = await jobs.CreateAsync(
                new CreateLocalJobRequest(
                    LocalDocumentLibraryService.DocumentIngestionJobType,
                    payloadJson,
                    Priority: 30,
                    MaxRetries: 0),
                cancellationToken);
            ImportedDocument? queued = await documents.SetStatusAsync(id, DocumentStatus.Queued, job.Id, lastError: null, cancellationToken);
            return queued is null ? CreateNotFoundProblem("Documento") : Results.Ok(queued);
        });

        app.MapPost("/api/documents/{id:long}/embed", async (
            long id,
            IDocumentLibraryService documents,
            IOllamaSettingsService settings,
            IOllamaClient ollamaClient,
            ILocalJobQueue jobs,
            IEmbeddingRepository embeddings,
            IVectorSearchService vectorSearch,
            CancellationToken cancellationToken) =>
        {
            ImportedDocument? document = await documents.GetAsync(id, cancellationToken);
            if (document is null)
            {
                return CreateNotFoundProblem("Documento");
            }

            LocalJob? activeJob = await GetActiveDocumentJobAsync(document, jobs, cancellationToken);
            if (activeJob is not null)
            {
                return CreateConflictProblem(
                    "Documento occupato",
                    $"Il documento ha gia un job attivo: {activeJob.Type}.",
                    "document_job_active");
            }

            OllamaSettings currentSettings = await settings.GetAsync(cancellationToken);
            string model;
            try
            {
                model = OllamaSettingsService.NormalizeRequiredModelName(currentSettings.DefaultEmbeddingModel);
            }
            catch (OllamaApiException ex)
            {
                return MapOllamaException(ex);
            }

            if (document.ChunkCount == 0)
            {
                return CreateConflictProblem(
                    "Documento non indicizzato",
                    "Esegui prima l'ingestion del documento: gli embedding vengono generati solo sui chunk.",
                    "document_not_indexed");
            }

            try
            {
                IReadOnlyList<OllamaModelSummary> models = await ollamaClient.ListModelsAsync(cancellationToken);
                bool modelInstalled = models.Any(installed =>
                    string.Equals(installed.Name, model, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(installed.Model, model, StringComparison.OrdinalIgnoreCase));
                if (!modelInstalled)
                {
                    return CreateProblem(
                        "Modello embedding assente",
                        $"Il modello embedding '{model}' non e installato in Ollama.",
                        StatusCodes.Status404NotFound,
                        "ollama_embedding_model_not_found");
                }
            }
            catch (OllamaApiException ex)
            {
                return MapOllamaException(ex);
            }

            string payloadJson = System.Text.Json.JsonSerializer.Serialize(new DocumentEmbeddingJobPayload(id, model));
            LocalJob job = await jobs.CreateAsync(
                new CreateLocalJobRequest(
                    DocumentEmbeddingJobHandler.DocumentEmbeddingJobType,
                    payloadJson,
                    Priority: 10,
                    MaxRetries: 2),
                cancellationToken);
            await documents.SetStatusAsync(id, DocumentStatus.Processing, job.Id, lastError: null, cancellationToken);

            DocumentEmbeddingStatusResponse status = await BuildEmbeddingStatusResponseAsync(
                id,
                currentSettings.DefaultEmbeddingModel,
                documents,
                embeddings,
                jobs,
                vectorSearch,
                cancellationToken);
            return Results.Ok(status);
        });

        app.MapGet("/api/documents/{id:long}/embedding-status", async (
            long id,
            IDocumentLibraryService documents,
            IOllamaSettingsService settings,
            ILocalJobQueue jobs,
            IEmbeddingRepository embeddings,
            IVectorSearchService vectorSearch,
            CancellationToken cancellationToken) =>
        {
            ImportedDocument? document = await documents.GetAsync(id, cancellationToken);
            if (document is null)
            {
                return CreateNotFoundProblem("Documento");
            }

            OllamaSettings currentSettings = await settings.GetAsync(cancellationToken);
            return Results.Ok(await BuildEmbeddingStatusResponseAsync(
                id,
                currentSettings.DefaultEmbeddingModel,
                documents,
                embeddings,
                jobs,
                vectorSearch,
                cancellationToken));
        });

        MapDocumentPipelineEndpoints(app);
    }

}
