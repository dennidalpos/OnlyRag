using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ocr;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Infrastructure.Vector;
using OnlyRag.Worker;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    private const int DefaultDocumentPreviewPageSize = 1;
    private const int MaxDocumentPreviewPageSize = 10;

    private static void MapDocumentEndpoints(WebApplication app)
    {
        MapDocumentImportEndpoints(app);

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
            OcrProcessingSettingsStore ocrProcessingSettings,
            CancellationToken cancellationToken) =>
        {
            ImportedDocument? existing = await documents.GetAsync(id, cancellationToken);
            if (existing is null)
            {
                return CreateNotFoundProblem("Documento");
            }

            await CancelDocumentJobIfNeededAsync(existing, jobs, cancellationRegistry, cancellationToken);
            string resolvedOcrLanguage = await ResolveOcrLanguageAsync(
                ocrLanguage,
                ocrProcessingSettings,
                cancellationToken);
            ImportedDocument? queued = await documents.QueueForIndexingAsync(id, resolvedOcrLanguage, cancellationToken);
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
            OcrProcessingSettingsStore ocrProcessingSettings,
            CancellationToken cancellationToken) =>
        {
            ImportedDocument? document = await documents.GetAsync(id, cancellationToken);
            if (document is null)
            {
                return CreateNotFoundProblem("Documento");
            }

            await CancelDocumentJobIfNeededAsync(document, jobs, cancellationRegistry, cancellationToken);
            string resolvedOcrLanguage = await ResolveOcrLanguageAsync(
                ocrLanguage,
                ocrProcessingSettings,
                cancellationToken);
            string payloadJson = System.Text.Json.JsonSerializer.Serialize(new DocumentIngestionJobPayload(
                document.Id,
                document.DocumentUid,
                document.OriginalFileName,
                document.Sha256 ?? string.Empty,
                ForceOcr: force ?? false,
                OcrLanguage: resolvedOcrLanguage));
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
            IQdrantVectorStore vectorSearch,
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
                return MapOllamaException(ex, app.Services, "/api/documents/{id}/embed settings");
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
                return MapOllamaException(ex, app.Services, "/api/documents/{id}/embed model check");
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
            IQdrantVectorStore vectorSearch,
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

    private static async Task<string> ResolveOcrLanguageAsync(
        string? requestedLanguage,
        OcrProcessingSettingsStore ocrProcessingSettings,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedLanguage))
        {
            return OcrLanguages.NormalizeCode(requestedLanguage);
        }

        OcrProcessingSettings settings = await ocrProcessingSettings.GetAsync(cancellationToken);
        return OcrLanguages.NormalizeCode(settings.Language);
    }

}
