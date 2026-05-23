using System.Text.Json;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Api;

internal sealed class DocumentEmbeddingJobHandler : ILocalJobHandler
{
    public const string DocumentEmbeddingJobType = "document-embedding";

    private readonly IDocumentLibraryService documents;
    private readonly IEmbeddingRepository embeddings;
    private readonly IOllamaClient ollamaClient;
    private readonly IOllamaSettingsService settingsService;
    private readonly IPerformanceSettingsService performanceSettings;

    public DocumentEmbeddingJobHandler(
        IDocumentLibraryService documents,
        IEmbeddingRepository embeddings,
        IOllamaClient ollamaClient,
        IOllamaSettingsService settingsService,
        IPerformanceSettingsService performanceSettings)
    {
        this.documents = documents;
        this.embeddings = embeddings;
        this.ollamaClient = ollamaClient;
        this.settingsService = settingsService;
        this.performanceSettings = performanceSettings;
    }

    public string Type => DocumentEmbeddingJobType;

    public async Task ExecuteAsync(LocalJob job, ILocalJobQueue queue, CancellationToken cancellationToken)
    {
        DocumentEmbeddingJobPayload? payload = JsonSerializer.Deserialize<DocumentEmbeddingJobPayload>(job.PayloadJson);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Model))
        {
            await queue.FailAsync(job.Id, "Payload job embedding non valido.", retryable: false, cancellationToken);
            return;
        }

        ImportedDocument? document = await documents.GetAsync(payload.DocumentId, cancellationToken);
        if (document is null)
        {
            await queue.CancelAsync(job.Id, cancellationToken);
            return;
        }

        string model = OllamaSettingsService.NormalizeRequiredModelName(payload.Model);
        await settingsService.GetAsync(cancellationToken);
        int batchSize = (await performanceSettings.GetAsync(cancellationToken)).EmbeddingBatchSize;
        DocumentEmbeddingCheckpoint checkpoint = ReadCheckpoint(job.CheckpointJson, document.Id, model);

        int? embeddingNumCtx = (await settingsService.GetAsync(cancellationToken)).EmbeddingNumCtx;

        try
        {
            await documents.SetStatusAsync(document.Id, DocumentStatus.Processing, job.Id, lastError: null, cancellationToken);
            await EmbedFromCheckpointAsync(document, model, batchSize, embeddingNumCtx, checkpoint, job, queue, cancellationToken);
            await documents.SetStatusAsync(document.Id, DocumentStatus.Indexed, currentJobId: null, lastError: null, cancellationToken);
        }
        catch (OllamaApiException ex) when (ex.Kind == OllamaErrorKind.ContextLengthExceeded)
        {
            string message = "Chunk troppo lungo per la finestra di contesto del modello. Imposta un num_ctx più alto nelle impostazioni o riduci la dimensione dei chunk.";
            await documents.SetStatusAsync(document.Id, DocumentStatus.Failed, job.Id, message, cancellationToken);
            await queue.FailAsync(job.Id, message, retryable: false, cancellationToken);
        }
        catch (OllamaApiException ex)
        {
            bool retryable = ex.Kind is OllamaErrorKind.Timeout
                or OllamaErrorKind.Unreachable
                or OllamaErrorKind.UnexpectedResponse;
            LocalJob? failed = await queue.FailAsync(job.Id, ex.Message, retryable, cancellationToken);
            if (failed?.Status is JobStatus.Failed)
            {
                await documents.SetStatusAsync(document.Id, DocumentStatus.Failed, job.Id, ex.Message, cancellationToken);
            }
        }
        catch (InvalidOperationException ex)
        {
            string message = UserFacingErrorText.FromExternalDetail(
                ex.Message,
                "Embedding non completato. Dettagli tecnici disponibili nei log locali.");
            await documents.SetStatusAsync(document.Id, DocumentStatus.Failed, job.Id, message, cancellationToken);
            await queue.FailAsync(job.Id, message, retryable: false, cancellationToken);
        }
    }

    private async Task EmbedFromCheckpointAsync(
        ImportedDocument document,
        string model,
        int batchSize,
        int? configuredNumCtx,
        DocumentEmbeddingCheckpoint checkpoint,
        LocalJob job,
        ILocalJobQueue queue,
        CancellationToken cancellationToken)
    {
        int afterChunkIndex = checkpoint.NextChunkIndex;
        bool restartedScan = afterChunkIndex == 0;

        while (true)
        {
            IReadOnlyList<DocumentChunkForEmbedding> chunks = await embeddings.ListChunksNeedingEmbeddingAsync(
                document.Id,
                model,
                afterChunkIndex,
                batchSize,
                cancellationToken);

            if (chunks.Count == 0)
            {
                DocumentEmbeddingStatusSnapshot status = await embeddings.GetDocumentEmbeddingStatusAsync(
                    document.Id,
                    model,
                    cancellationToken);

                if (!restartedScan && status.EmbeddedChunkCount < status.ChunkCount)
                {
                    afterChunkIndex = 0;
                    restartedScan = true;
                    continue;
                }

                await queue.SaveCheckpointAsync(
                    job.Id,
                    new LocalJobCheckpoint(
                        100,
                        $"Embedding completati: {status.EmbeddedChunkCount}/{status.ChunkCount} chunk",
                        JsonSerializer.Serialize(new DocumentEmbeddingCheckpoint(
                            Version: 1,
                            document.Id,
                            model,
                            NextChunkIndex: int.MaxValue,
                            EmbeddedChunkCount: status.EmbeddedChunkCount,
                            Mode: "completed"))),
                    cancellationToken);
                return;
            }

            int numCtx = ComputeNumCtx(chunks, configuredNumCtx);
            IReadOnlyList<IReadOnlyList<float>> vectors = await ollamaClient.GenerateEmbeddingsAsync(
                model,
                chunks.Select(chunk => chunk.Content).ToArray(),
                numCtx,
                cancellationToken);

            for (int index = 0; index < chunks.Count; index++)
            {
                IReadOnlyList<float> vector = vectors[index];
                if (vector.Count == 0)
                {
                    throw new InvalidOperationException("Ollama ha restituito un embedding vuoto.");
                }

                await embeddings.UpsertEmbeddingAsync(
                    chunks[index].Id,
                    model,
                    chunks[index].ContentHash,
                    vector,
                    cancellationToken);
            }

            DocumentEmbeddingStatusSnapshot progress = await embeddings.GetDocumentEmbeddingStatusAsync(
                document.Id,
                model,
                cancellationToken);
            afterChunkIndex = chunks[^1].ChunkIndex + 1;
            int progressPercent = progress.ChunkCount == 0
                ? 100
                : (int)Math.Round(progress.EmbeddedChunkCount * 100d / progress.ChunkCount);

            await queue.SaveCheckpointAsync(
                job.Id,
                new LocalJobCheckpoint(
                    progressPercent,
                    $"Embedding chunk {progress.EmbeddedChunkCount}/{progress.ChunkCount}",
                    JsonSerializer.Serialize(new DocumentEmbeddingCheckpoint(
                        Version: 1,
                        document.Id,
                        model,
                        afterChunkIndex,
                        progress.EmbeddedChunkCount,
                        Mode: "running"))),
                cancellationToken);
        }
    }

    private static int ComputeNumCtx(IReadOnlyList<DocumentChunkForEmbedding> chunks, int? configuredNumCtx)
    {
        int maxChars = chunks.Max(c => c.Content.Length);
        // chars/3 + 256 head-room gives a safe token estimate for multilingual text
        int needed = (int)Math.Ceiling(maxChars / 3.0) + 256;

        if (configuredNumCtx.HasValue)
        {
            if (needed > configuredNumCtx.Value)
            {
                throw new OllamaApiException(
                    OllamaErrorKind.ContextLengthExceeded,
                    $"Un chunk richiede almeno {needed} token di contesto, ma la finestra configurata è {configuredNumCtx.Value}. "
                    + "Aumenta num_ctx nelle impostazioni oppure imposta la modalità Automatica.");
            }

            return configuredNumCtx.Value;
        }

        return Math.Max(needed, 512);
    }

    private static DocumentEmbeddingCheckpoint ReadCheckpoint(
        string checkpointJson,
        long documentId,
        string model)
    {
        if (!string.IsNullOrWhiteSpace(checkpointJson))
        {
            try
            {
                DocumentEmbeddingCheckpoint? checkpoint = JsonSerializer.Deserialize<DocumentEmbeddingCheckpoint>(checkpointJson);
                if (checkpoint is not null
                    && checkpoint.DocumentId == documentId
                    && string.Equals(checkpoint.Model, model, StringComparison.OrdinalIgnoreCase))
                {
                    return checkpoint;
                }
            }
            catch (JsonException)
            {
            }
        }

        return new DocumentEmbeddingCheckpoint(
            Version: 1,
            documentId,
            model,
            NextChunkIndex: 0,
            EmbeddedChunkCount: 0,
            Mode: "new");
    }

    private sealed record DocumentEmbeddingCheckpoint(
        int Version,
        long DocumentId,
        string Model,
        int NextChunkIndex,
        int EmbeddedChunkCount,
        string Mode);
}
