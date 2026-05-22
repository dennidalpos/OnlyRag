using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    private static void MapDocumentPipelineEndpoints(WebApplication app)
    {
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
                return CreateNotFoundProblem("Documento");
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

            return Results.Ok(BuildPipelineStatus(id, document, activeJob, currentSettings, ocrStatus, embeddingStatus));
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
                return CreateNotFoundProblem("Documento");
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

    private static DocumentPipelineStatus BuildPipelineStatus(
        long id,
        ImportedDocument document,
        LocalJob? activeJob,
        OllamaSettings currentSettings,
        DocumentOcrStatusResponse ocrStatus,
        DocumentEmbeddingStatusResponse embeddingStatus)
    {
        PipelinePhaseInfo importPhase = new(PhaseState.Completed, null, document.CreatedAtUtc);
        PipelinePhaseInfo analysisPhase = document.Status == DocumentStatus.Imported
            ? new(PhaseState.Todo, null, null)
            : new(PhaseState.Completed, null, document.UpdatedAtUtc);
        PipelinePhaseInfo ocrPhase = BuildOcrPhase(document, activeJob, ocrStatus);
        bool isProcessing = document.Status == DocumentStatus.Processing;
        bool hasPages = document.PageCount > 0;
        bool hasChunks = document.ChunkCount > 0;
        PipelinePhaseInfo textExtractionPhase = hasPages
            ? new(PhaseState.Completed, null, null)
            : isProcessing
                ? new(PhaseState.InProgress, null, null)
                : document.Status == DocumentStatus.Failed
                    ? new(PhaseState.Failed, document.LastError, null)
                    : new(PhaseState.Todo, null, null);
        PipelinePhaseInfo chunkingPhase = hasChunks
            ? new(PhaseState.Completed, null, null)
            : isProcessing
                ? new(PhaseState.InProgress, null, null)
                : document.Status == DocumentStatus.Failed
                    ? new(PhaseState.Failed, document.LastError, null)
                    : new(PhaseState.Todo, null, null);
        PipelinePhaseInfo embeddingPhase = BuildEmbeddingPhase(document, currentSettings, embeddingStatus);
        bool isReady = document.Status == DocumentStatus.Indexed
            && hasChunks
            && embeddingStatus.State == "Complete";
        PhaseState overall = document.Status == DocumentStatus.Failed
            ? PhaseState.Failed
            : isReady
                ? PhaseState.Completed
                : activeJob is not null || isProcessing
                    ? PhaseState.InProgress
                    : hasChunks
                        ? PhaseState.InProgress
                        : PhaseState.Todo;

        return new DocumentPipelineStatus(
            id,
            "Auto",
            importPhase,
            analysisPhase,
            ocrPhase,
            textExtractionPhase,
            chunkingPhase,
            embeddingPhase,
            overall,
            activeJob?.Id,
            activeJob?.Type);
    }

    private static PipelinePhaseInfo BuildOcrPhase(
        ImportedDocument document,
        LocalJob? activeJob,
        DocumentOcrStatusResponse ocrStatus)
    {
        if (!IsOcrCandidate(document.FileExtension))
        {
            return new PipelinePhaseInfo(PhaseState.Skipped, null, null);
        }

        if (activeJob?.Type == LocalDocumentLibraryService.DocumentIngestionJobType)
        {
            return new PipelinePhaseInfo(PhaseState.InProgress, null, null);
        }

        return ocrStatus.State switch
        {
            "Complete" => new PipelinePhaseInfo(PhaseState.Completed, null, null),
            "Failed" => new PipelinePhaseInfo(PhaseState.Failed, ocrStatus.LastError, null),
            _ when ocrStatus.OcrPageCount > 0 => new PipelinePhaseInfo(PhaseState.Completed, null, null),
            _ => new PipelinePhaseInfo(PhaseState.Todo, null, null)
        };
    }

    private static PipelinePhaseInfo BuildEmbeddingPhase(
        ImportedDocument document,
        OllamaSettings currentSettings,
        DocumentEmbeddingStatusResponse embeddingStatus)
    {
        if (string.IsNullOrWhiteSpace(currentSettings.DefaultEmbeddingModel))
        {
            return new PipelinePhaseInfo(PhaseState.Skipped, "Modello embedding non configurato.", null);
        }

        return embeddingStatus.State switch
        {
            "Complete" => new PipelinePhaseInfo(PhaseState.Completed, null, embeddingStatus.LastEmbeddedAtUtc),
            "Running" or "Pausing" or "Pending" or "Paused" => new PipelinePhaseInfo(PhaseState.InProgress, null, null),
            "Failed" => new PipelinePhaseInfo(PhaseState.Failed, document.LastError, null),
            "Partial" => new PipelinePhaseInfo(
                PhaseState.Obsolete,
                "Embedding parziali: rigenera per completare la ricerca semantica.",
                embeddingStatus.LastEmbeddedAtUtc),
            _ => new PipelinePhaseInfo(PhaseState.Todo, null, null)
        };
    }
}
