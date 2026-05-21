using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
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
        return currentJob?.Status is JobStatus.Pending or JobStatus.Running or JobStatus.Pausing or JobStatus.Paused
            ? currentJob
            : null;
    }

    private static async Task<DocumentEmbeddingStatusResponse> BuildEmbeddingStatusResponseAsync(
        long documentId,
        string? model,
        IDocumentLibraryService documents,
        IEmbeddingRepository embeddings,
        ILocalJobQueue jobs,
        IVectorSearchService vectorSearch,
        CancellationToken cancellationToken)
    {
        ImportedDocument? document = await documents.GetAsync(documentId, cancellationToken);
        if (document is null)
        {
            throw new InvalidOperationException("Documento non trovato.");
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

    private static async Task CancelDocumentJobIfNeededAsync(
        ImportedDocument document,
        ILocalJobQueue jobs,
        RunningJobCancellationRegistry cancellationRegistry,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(document.CurrentJobId))
        {
            return;
        }

        LocalJob? currentJob = await jobs.GetAsync(document.CurrentJobId, cancellationToken);
        if (currentJob?.Status is not (JobStatus.Pending or JobStatus.Running or JobStatus.Pausing or JobStatus.Paused))
        {
            return;
        }

        await jobs.CancelAsync(document.CurrentJobId, cancellationToken);
        cancellationRegistry.Cancel(document.CurrentJobId);

        // Wait for the job worker to release its SQLite connections before the caller modifies
        // shared tables. Without this, the shared-cache connection used by the running job may
        // still hold an open transaction, causing "SQL logic error" on the subsequent delete.
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (cancellationRegistry.IsRunning(document.CurrentJobId))
        {
            if (timeout.Token.IsCancellationRequested)
            {
                throw new TimeoutException($"Il job {document.CurrentJobId} non si e fermato entro 10 secondi. Riprovare.");
            }

            await Task.Delay(80, timeout.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
