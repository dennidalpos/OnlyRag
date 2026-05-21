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
            return document is null ? Results.NotFound() : Results.Ok(document);
        });

        app.MapPost("/api/documents/import", async (
            HttpRequest request,
            IDocumentLibraryService documents,
            LocalDocumentStorageGuard storageGuard,
            CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.Problem(
                    title: "Richiesta import non valida",
                    detail: "Usa multipart/form-data con uno o piu file.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (request.ContentLength > storageGuard.Limits.MaxRequestBodySizeBytes)
            {
                return Results.Problem(
                    title: "Import troppo grande",
                    detail: $"La richiesta supera il limite di {LocalDocumentLibraryLimits.FormatBytes(storageGuard.Limits.MaxRequestBodySizeBytes)}.",
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            IFormCollection form;
            try
            {
                form = await request.ReadFormAsync(cancellationToken);
            }
            catch (BadHttpRequestException)
            {
                return Results.Problem(
                    title: "Import troppo grande",
                    detail: "La richiesta multipart supera i limiti configurati.",
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            }
            catch (InvalidDataException)
            {
                return Results.Problem(
                    title: "Import troppo grande",
                    detail: "La richiesta multipart supera i limiti configurati.",
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            if (form.Files.Count == 0)
            {
                return Results.Problem(
                    title: "Nessun file selezionato",
                    detail: "Seleziona almeno un file da importare.",
                    statusCode: StatusCodes.Status400BadRequest);
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
                    return Results.Problem(
                        title: "File vuoto",
                        detail: $"Il file '{file.FileName}' e vuoto.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                try
                {
                    await using Stream stream = file.OpenReadStream();
                    importedDocuments.Add(await documents.ImportAsync(stream, file.FileName, forceOcr, ocrLanguage, cancellationToken));
                }
                catch (ArgumentException ex)
                {
                    return Results.Problem(
                        title: "Import non valido",
                        detail: ex.Message,
                        statusCode: StatusCodes.Status400BadRequest);
                }
                catch (DocumentStorageLimitException ex)
                {
                    return MapDocumentStorageLimitException(ex);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Problem(
                        title: "Path documento non valido",
                        detail: ex.Message,
                        statusCode: StatusCodes.Status400BadRequest);
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
                return Results.NotFound();
            }

            await CancelDocumentJobIfNeededAsync(existing, jobs, cancellationRegistry, cancellationToken);
            ImportedDocument? deleted = await documents.DeleteAsync(id, cancellationToken);
            return deleted is null ? Results.NotFound() : Results.Ok(deleted);
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
                return Results.NotFound();
            }

            await CancelDocumentJobIfNeededAsync(existing, jobs, cancellationRegistry, cancellationToken);
            ImportedDocument? queued = await documents.QueueForIndexingAsync(id, OcrLanguages.NormalizeCode(ocrLanguage), cancellationToken);
            return queued is null ? Results.NotFound() : Results.Ok(queued);
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
                return Results.NotFound();
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
                return Results.NotFound();
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
            return queued is null ? Results.NotFound() : Results.Ok(queued);
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
                return Results.NotFound();
            }

            LocalJob? activeJob = await GetActiveDocumentJobAsync(document, jobs, cancellationToken);
            if (activeJob is not null)
            {
                return Results.Problem(
                    title: "Documento occupato",
                    detail: $"Il documento ha gia un job attivo: {activeJob.Type}.",
                    statusCode: StatusCodes.Status409Conflict);
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
                return Results.Problem(
                    title: "Documento non indicizzato",
                    detail: "Esegui prima l'ingestion del documento: gli embedding vengono generati solo sui chunk.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            try
            {
                IReadOnlyList<OllamaModelSummary> models = await ollamaClient.ListModelsAsync(cancellationToken);
                bool modelInstalled = models.Any(installed =>
                    string.Equals(installed.Name, model, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(installed.Model, model, StringComparison.OrdinalIgnoreCase));
                if (!modelInstalled)
                {
                    return Results.Problem(
                        title: "Modello embedding assente",
                        detail: $"Il modello embedding '{model}' non e installato in Ollama.",
                        statusCode: StatusCodes.Status404NotFound);
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
                return Results.NotFound();
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

        app.MapGet("/api/documents/{id:long}/pipeline-status", async (
            long id,
            IDocumentLibraryService documents,
            IDocumentRepository documentRepository,
            IOllamaSettingsService settings,
            ILocalJobQueue jobs,
            IEmbeddingRepository embeddings,
            IVectorSearchService vectorSearch,
            CancellationToken cancellationToken) =>
        {
            ImportedDocument? document = await documents.GetAsync(id, cancellationToken);
            if (document is null)
            {
                return Results.NotFound();
            }

            OllamaSettings currentSettings = await settings.GetAsync(cancellationToken);
            LocalJob? activeJob = await GetActiveDocumentJobAsync(document, jobs, cancellationToken);

            DocumentOcrStatusResponse ocrStatus = await documentRepository.GetOcrStatusAsync(
                id,
                activeJob?.Type == LocalDocumentLibraryService.DocumentIngestionJobType ? activeJob.Id : null,
                activeJob?.Type == LocalDocumentLibraryService.DocumentIngestionJobType ? activeJob.CurrentStep : null,
                cancellationToken);

            DocumentEmbeddingStatusResponse embeddingStatus = await BuildEmbeddingStatusResponseAsync(
                id,
                currentSettings.DefaultEmbeddingModel,
                documents,
                embeddings,
                jobs,
                vectorSearch,
                cancellationToken);

            string ocrPolicy = "Auto";
            bool isOcrCandidate = IsOcrCandidate(document.FileExtension);

            PipelinePhaseInfo importPhase = new(PhaseState.Completed, null, document.CreatedAtUtc);
            PipelinePhaseInfo analysisPhase = document.Status == DocumentStatus.Imported
                ? new(PhaseState.Todo, null, null)
                : new(PhaseState.Completed, null, document.UpdatedAtUtc);

            PipelinePhaseInfo ocrPhase;
            if (!isOcrCandidate)
            {
                ocrPhase = new(PhaseState.Skipped, null, null);
            }
            else if (activeJob?.Type == LocalDocumentLibraryService.DocumentIngestionJobType)
            {
                ocrPhase = new(PhaseState.InProgress, null, null);
            }
            else if (ocrStatus.State == "Complete")
            {
                ocrPhase = new(PhaseState.Completed, null, null);
            }
            else if (ocrStatus.State == "Failed")
            {
                ocrPhase = new(PhaseState.Failed, ocrStatus.LastError, null);
            }
            else if (ocrStatus.OcrPageCount > 0)
            {
                ocrPhase = new(PhaseState.Completed, null, null);
            }
            else
            {
                ocrPhase = new(PhaseState.Todo, null, null);
            }

            bool isProcessing = document.Status == DocumentStatus.Processing;
            bool hasPages = document.PageCount > 0;

            PipelinePhaseInfo textExtractionPhase = hasPages
                ? new(PhaseState.Completed, null, null)
                : isProcessing
                    ? new(PhaseState.InProgress, null, null)
                    : document.Status == DocumentStatus.Failed
                        ? new(PhaseState.Failed, document.LastError, null)
                        : new(PhaseState.Todo, null, null);

            bool hasChunks = document.ChunkCount > 0;
            PipelinePhaseInfo chunkingPhase = hasChunks
                ? new(PhaseState.Completed, null, null)
                : isProcessing
                    ? new(PhaseState.InProgress, null, null)
                    : document.Status == DocumentStatus.Failed
                        ? new(PhaseState.Failed, document.LastError, null)
                        : hasPages
                            ? new(PhaseState.Todo, null, null)
                            : new(PhaseState.Todo, null, null);

            PipelinePhaseInfo embeddingPhase;
            string embState = embeddingStatus.State;
            if (string.IsNullOrWhiteSpace(currentSettings.DefaultEmbeddingModel))
            {
                embeddingPhase = new(PhaseState.Skipped, "Modello embedding non configurato.", null);
            }
            else if (embState is "Complete")
            {
                embeddingPhase = new(PhaseState.Completed, null, embeddingStatus.LastEmbeddedAtUtc);
            }
            else if (embState is "Running" or "Pausing" or "Pending" or "Paused")
            {
                embeddingPhase = new(PhaseState.InProgress, null, null);
            }
            else if (embState is "Failed")
            {
                embeddingPhase = new(PhaseState.Failed, document.LastError, null);
            }
            else if (embState is "NotIndexed")
            {
                embeddingPhase = new(PhaseState.Todo, null, null);
            }
            else if (embState is "Partial")
            {
                embeddingPhase = new(PhaseState.Obsolete, "Embedding parziali: rigenera per completare la ricerca semantica.", embeddingStatus.LastEmbeddedAtUtc);
            }
            else
            {
                embeddingPhase = new(PhaseState.Todo, null, null);
            }

            bool isReady = document.Status == DocumentStatus.Indexed
                && hasChunks
                && embState == "Complete";

            PhaseState overall = document.Status == DocumentStatus.Failed
                ? PhaseState.Failed
                : isReady
                    ? PhaseState.Completed
                    : activeJob is not null || isProcessing
                        ? PhaseState.InProgress
                        : hasChunks
                            ? PhaseState.InProgress
                            : PhaseState.Todo;

            return Results.Ok(new DocumentPipelineStatus(
                id,
                ocrPolicy,
                importPhase,
                analysisPhase,
                ocrPhase,
                textExtractionPhase,
                chunkingPhase,
                embeddingPhase,
                overall,
                activeJob?.Id,
                activeJob?.Type));
        });

        app.MapGet("/api/documents/{id:long}/preview", async (
            long id,
            int? page,
            int? pageSize,
            IDocumentLibraryService documents,
            IDocumentRepository documentRepository,
            CancellationToken cancellationToken) =>
        {
            ImportedDocument? document = await documents.GetAsync(id, cancellationToken);
            if (document is null)
            {
                return Results.NotFound();
            }

            int requestedPage = Math.Max(1, page ?? 1);
            if (document.PageCount > 0)
            {
                requestedPage = Math.Min(requestedPage, document.PageCount);
            }

            int requestedPageSize = Math.Clamp(
                pageSize ?? DefaultDocumentPreviewPageSize,
                1,
                MaxDocumentPreviewPageSize);
            IReadOnlyList<DocumentPageInfo> pages = await documentRepository.GetPagesAsync(
                id,
                requestedPage,
                requestedPageSize,
                cancellationToken);
            return Results.Ok(new DocumentPreviewResponse(
                document.Id,
                document.OriginalFileName,
                document.MimeType,
                document.FileExtension,
                document.FileSizeBytes,
                document.PageCount,
                document.ChunkCount,
                document.Status.ToString(),
                requestedPage,
                requestedPageSize,
                pages.Count,
                pages));
        });
    }

}
