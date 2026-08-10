using System.Text.Json;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ingestion;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Api;

internal sealed class DocumentIngestionJobHandler : ILocalJobHandler
{
    private readonly IDocumentLibraryService documents;
    private readonly IDocumentIngestionService ingestion;
    private readonly InProcessBackendDescriptor descriptor;
    private readonly IOllamaSettingsService ollamaSettings;

    public DocumentIngestionJobHandler(
        IDocumentLibraryService documents,
        IDocumentIngestionService ingestion,
        InProcessBackendDescriptor descriptor,
        IOllamaSettingsService ollamaSettings)
    {
        this.documents = documents;
        this.ingestion = ingestion;
        this.descriptor = descriptor;
        this.ollamaSettings = ollamaSettings;
    }

    public string Type => LocalDocumentLibraryService.DocumentIngestionJobType;

    public async Task ExecuteAsync(LocalJob job, ILocalJobQueue queue, CancellationToken cancellationToken)
    {
        DocumentIngestionJobPayload? payload = JsonSerializer.Deserialize<DocumentIngestionJobPayload>(job.PayloadJson);
        if (payload is null)
        {
            await queue.FailAsync(job.Id, "Payload job documento non valido.", retryable: false, cancellationToken);
            return;
        }

        ImportedDocument? document = await documents.GetAsync(payload.DocumentId, cancellationToken);
        if (document is null)
        {
            await queue.CancelAsync(job.Id, cancellationToken);
            return;
        }

        DocumentIngestionCheckpoint? checkpoint = DocumentIngestionService.ReadCheckpoint(job.CheckpointJson);

        try
        {
            await documents.SetStatusAsync(document.Id, DocumentStatus.Processing, job.Id, lastError: null, cancellationToken);
            var channel = System.Threading.Channels.Channel.CreateBounded<DocumentIngestionProgress>(
                new System.Threading.Channels.BoundedChannelOptions(100)
                {
                    FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true
                });

            using CancellationTokenSource progressCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            CancellationToken progressToken = progressCancellation.Token;
            var consumerTask = Task.Run(async () =>
            {
                await foreach (var progress in channel.Reader.ReadAllAsync(progressToken))
                {
                    string checkpointJson = JsonSerializer.Serialize(progress.Checkpoint);
                    await queue.SaveCheckpointAsync(
                        job.Id,
                        new LocalJobCheckpoint(progress.ProgressPercent, progress.CurrentStep, checkpointJson),
                        progressToken);
                }
            }, progressToken);

            DocumentIngestionResult result;
            try
            {
                result = await ingestion.IngestAsync(
                    document,
                    checkpoint,
                    async (progress, token) =>
                    {
                        await channel.Writer.WriteAsync(progress, token);
                    },
                    payload.ForceOcr,
                    payload.OcrLanguage,
                    progressToken);

                channel.Writer.TryComplete();
                await consumerTask;
            }
            finally
            {
                channel.Writer.TryComplete();
                progressCancellation.Cancel();
                try
                {
                    await consumerTask;
                }
                catch (OperationCanceledException) when (progressToken.IsCancellationRequested)
                {
                    // The progress consumer is cancelled as part of pipeline cleanup.
                }
            }

            DocumentIngestionCheckpoint completedCheckpoint = new(
                Version: 1,
                document.Id,
                NextBlock: result.PageCount + 1,
                PageCount: result.PageCount,
                NextChunkOrdinal: result.ChunkCount,
                Mode: "completed");
            await queue.SaveCheckpointAsync(
                job.Id,
                new LocalJobCheckpoint(
                    100,
                    $"Ingestion completata: {result.PageCount} {DescribeIndexedUnit(document.FileExtension)}, {result.ChunkCount} chunk",
                    JsonSerializer.Serialize(completedCheckpoint)),
                cancellationToken);
            await documents.SetStatusAsync(document.Id, DocumentStatus.Indexed, currentJobId: null, lastError: null, cancellationToken);

            if (result.ChunkCount > 0)
            {
                await TryQueueEmbeddingAsync(document.Id, queue, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or NotSupportedException)
        {
            string message = UserFacingErrorText.FromExternalDetail(
                ex.Message,
                "Documento non indicizzato. Dettagli tecnici disponibili nei log locali.");
            BackendLog.WriteException(descriptor.StoragePaths, job.Id, $"Document ingestion failed for document {document.Id}.", ex);
            await documents.SetStatusAsync(document.Id, DocumentStatus.Failed, job.Id, message, cancellationToken);
            await queue.FailAsync(job.Id, message, retryable: false, cancellationToken);
        }
    }

    private async Task TryQueueEmbeddingAsync(long documentId, ILocalJobQueue queue, CancellationToken cancellationToken)
    {
        try
        {
            OllamaSettings settings = await ollamaSettings.GetAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(settings.DefaultEmbeddingModel))
            {
                return;
            }

            string model = OllamaSettingsService.NormalizeRequiredModelName(settings.DefaultEmbeddingModel);
            string payloadJson = JsonSerializer.Serialize(new DocumentEmbeddingJobPayload(documentId, model));
            await queue.CreateAsync(
                new CreateLocalJobRequest(
                    DocumentEmbeddingJobHandler.DocumentEmbeddingJobType,
                    payloadJson,
                    Priority: 10),
                cancellationToken);
        }
        catch (Exception ex)
        {
            BackendLog.WriteException(descriptor.StoragePaths, null, $"Auto-embedding skipped for document {documentId}.", ex);
        }
    }

    private static string DescribeIndexedUnit(string? extension)
    {
        return extension?.ToLowerInvariant() switch
        {
            ".docx" => "sezioni logiche",
            ".xlsx" => "fogli",
            ".pptx" => "slide",
            _ => "pagine"
        };
    }
}
