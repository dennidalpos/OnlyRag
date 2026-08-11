using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Application.Documents;
using OnlyRag.Application.Jobs;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ocr;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Infrastructure.Vector;
using OnlyRag.Jobs.Abstractions;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    private const int DefaultDocumentPreviewPageSize = 1;
    private const int MaxDocumentPreviewPageSize = 10;

    internal static void MapDocumentEndpoints(this WebApplication app)
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
            return document is null ? CreateNotFoundProblem("Document") : Results.Ok(document);
        });

        app.MapGet("/api/documents/{id:long}/archive-manifest", async (
            long id,
            IDocumentLibraryService documents,
            IArchiveManifestRepository archiveManifest,
            CancellationToken cancellationToken) =>
        {
            ImportedDocument? document = await documents.GetAsync(id, cancellationToken);
            if (document is null)
            {
                return CreateNotFoundProblem("Document");
            }

            if (document.FileExtension?.ToLowerInvariant() is not ".zip" and not ".tar" and not ".7z")
            {
                return Results.Ok(Array.Empty<ArchiveManifestEntry>());
            }

            return Results.Ok(await archiveManifest.ListAsync(id, cancellationToken));
        });

        app.MapDelete("/api/documents/{id:long}", async (
            long id,
            IDocumentLibraryService documents,
            IEmbeddingRepository embeddings,
            IQdrantVectorStore vectorSearch,
            JobApplicationService jobs,
            InProcessBackendDescriptor descriptor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            ImportedDocument? existing = await documents.GetAsync(id, cancellationToken);
            if (existing is null)
            {
                return CreateNotFoundProblem("Document");
            }

            await jobs.CancelAndWaitAsync(existing.CurrentJobId, cancellationToken);
            try
            {
                await DeleteDocumentVectorsAsync(id, embeddings, vectorSearch, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                BackendLog.WriteException(
                    descriptor.StoragePaths,
                    httpContext.TraceIdentifier,
                    $"Document vector cleanup failed for document {id}.",
                    ex);
                return CreateProblem(
                    "Vector cleanup failed",
                    "Document was not deleted to prevent orphaned vector data. Please try again after verifying Qdrant.",
                    StatusCodes.Status502BadGateway,
                    "document_vector_cleanup_failed",
                    httpContext.TraceIdentifier);
            }

            ImportedDocument? deleted = await documents.DeleteAsync(id, cancellationToken);
            return deleted is null ? CreateNotFoundProblem("Document") : Results.Ok(deleted);
        });

        app.MapPost("/api/documents/{id:long}/reindex", async (
            long id,
            string? ocrLanguage,
            DocumentPipelineApplicationService pipeline,
            OcrSettingsStore ocrSettings,
            CancellationToken cancellationToken) =>
        {
            string resolvedOcrLanguage = await ResolveOcrLanguageAsync(
                ocrLanguage,
                ocrSettings,
                cancellationToken);
            ImportedDocument? queued = await pipeline.QueueReindexAsync(id, resolvedOcrLanguage, cancellationToken);
            return queued is null ? CreateNotFoundProblem("Document") : Results.Ok(queued);
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
                return CreateNotFoundProblem("Document");
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
            DocumentPipelineApplicationService pipeline,
            OcrSettingsStore ocrSettings,
            CancellationToken cancellationToken) =>
        {
            string resolvedOcrLanguage = await ResolveOcrLanguageAsync(
                ocrLanguage,
                ocrSettings,
                cancellationToken);
            ImportedDocument? queued = await pipeline.QueueOcrAsync(
                id,
                force ?? false,
                resolvedOcrLanguage,
                cancellationToken);
            return queued is null ? CreateNotFoundProblem("Document") : Results.Ok(queued);
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
                return CreateNotFoundProblem("Document");
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
                        "Embedding model missing",
                        $"The embedding model '{model}' is not installed in Ollama.",
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
                    Priority: 10),
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
                return CreateNotFoundProblem("Document");
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
        OcrSettingsStore ocrSettings,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedLanguage))
        {
            return OcrLanguages.NormalizeCode(requestedLanguage);
        }

        OcrProcessingSettings settings = await ocrSettings.GetProcessingAsync(cancellationToken);
        return OcrLanguages.NormalizeCode(settings.Language);
    }

    private static bool IsOcrCandidate(string? fileExtension)
    {
        return fileExtension?.ToLowerInvariant() is ".pdf" or ".png" or ".jpg" or ".jpeg" or ".tif" or ".tiff" or ".bmp" or ".gif" or ".webp";
    }

    private static async Task<LocalJob?> GetActiveDocumentJobAsync(
        ImportedDocument document,
        ILocalJobQueue jobs,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(document.CurrentJobId))
        {
            return null;
        }

        LocalJob? currentJob = await jobs.GetAsync(document.CurrentJobId, cancellationToken);
        return currentJob?.Status.IsActive() == true
            ? currentJob
            : null;
    }

    private static async Task<DocumentEmbeddingStatusResponse> BuildEmbeddingStatusResponseAsync(
        long documentId,
        string? model,
        IDocumentLibraryService documents,
        IEmbeddingRepository embeddings,
        ILocalJobQueue jobs,
        IQdrantVectorStore vectorSearch,
        CancellationToken cancellationToken)
    {
        ImportedDocument? document = await documents.GetAsync(documentId, cancellationToken);
        if (document is null)
        {
            throw new InvalidOperationException("Document not found.");
        }

        DocumentEmbeddingStatusSnapshot snapshot = await embeddings.GetDocumentEmbeddingStatusAsync(
            documentId,
            model,
            cancellationToken);

        LocalJob? currentJob = null;
        if (!string.IsNullOrWhiteSpace(document.CurrentJobId))
        {
            LocalJob? job = await jobs.GetAsync(document.CurrentJobId, cancellationToken);
            if (job?.Type == DocumentEmbeddingJobHandler.DocumentEmbeddingJobType)
            {
                currentJob = job;
            }
        }

        int progressPercent = snapshot.ChunkCount == 0
            ? 0
            : (int)Math.Round(snapshot.EmbeddedChunkCount * 100d / snapshot.ChunkCount);
        if (currentJob is not null)
        {
            progressPercent = Math.Max(progressPercent, currentJob.ProgressPercent);
        }

        string state = ResolveEmbeddingState(model, snapshot, currentJob);

        return new DocumentEmbeddingStatusResponse(
            documentId,
            state,
            string.IsNullOrWhiteSpace(model) ? null : model,
            snapshot.ChunkCount,
            snapshot.EmbeddedChunkCount,
            Math.Clamp(progressPercent, 0, 100),
            currentJob?.Id,
            currentJob?.CurrentStep,
            vectorSearch.BackendName,
            snapshot.LastEmbeddedAtUtc);
    }

    private static string ResolveEmbeddingState(
        string? model,
        DocumentEmbeddingStatusSnapshot snapshot,
        LocalJob? currentJob)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return "NotConfigured";
        }

        if (currentJob is not null)
        {
            return currentJob.Status.ToString();
        }

        if (snapshot.ChunkCount == 0)
        {
            return "NotIndexed";
        }

        if (snapshot.EmbeddedChunkCount == 0)
        {
            return "NotStarted";
        }

        return snapshot.EmbeddedChunkCount >= snapshot.ChunkCount ? "Complete" : "Partial";
    }

    private static async Task DeleteDocumentVectorsAsync(
        long documentId,
        IEmbeddingRepository embeddings,
        IQdrantVectorStore vectorSearch,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DocumentVectorIndexReference> references =
            await embeddings.ListDocumentVectorIndexReferencesAsync(documentId, cancellationToken);

        foreach (DocumentVectorIndexReference reference in references)
        {
            await vectorSearch.DeleteDocumentAsync(
                reference.Model,
                reference.Dimensions,
                documentId,
                cancellationToken);
            await vectorSearch.OptimizeCollectionAsync(
                reference.Model,
                reference.Dimensions,
                cancellationToken);
        }
    }
}
