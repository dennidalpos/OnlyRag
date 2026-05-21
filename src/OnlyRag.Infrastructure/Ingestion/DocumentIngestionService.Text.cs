using System.Text;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Ingestion;

public sealed partial class DocumentIngestionService
{
    private const int TextBlockTargetCharacters = 64 * 1024;

    private async Task<DocumentIngestionResult> IngestTextFileAsync(
        ImportedDocument document,
        DocumentIngestionCheckpoint checkpoint,
        DocumentIngestionOptions options,
        Func<DocumentIngestionProgress, CancellationToken, Task> saveProgressAsync,
        CancellationToken cancellationToken)
    {
        FileInfo file = new(document.OriginalPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException("File originale documento non trovato.", document.OriginalPath);
        }

        int nextBlock = Math.Max(1, checkpoint.NextBlock);
        int pageCount = Math.Max(0, checkpoint.PageCount);
        int nextChunkOrdinal = Math.Max(0, checkpoint.NextChunkOrdinal);
        int chunkCount = nextChunkOrdinal;
        int blockNumber = 0;

        await using FileStream stream = new(
            document.OriginalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? block = await ReadNextTextBlockAsync(reader, cancellationToken);
            if (block is null)
            {
                break;
            }

            blockNumber++;
            if (blockNumber < nextBlock)
            {
                continue;
            }

            string normalizedBlock = block.Trim();
            if (normalizedBlock.Length == 0)
            {
                pageCount = blockNumber;
                DocumentIngestionCheckpoint emptyCheckpoint = checkpoint with
                {
                    NextBlock = blockNumber + 1,
                    PageCount = pageCount,
                    NextChunkOrdinal = nextChunkOrdinal
                };
                await saveProgressAsync(CreateProgress(document, file.Length, stream.Position, emptyCheckpoint), cancellationToken);
                continue;
            }

            IReadOnlyList<IngestedDocumentChunk> chunks = chunker.CreateChunks(
                normalizedBlock,
                blockNumber,
                blockNumber,
                nextChunkOrdinal,
                options);
            await documents.SaveIngestedPageAsync(
                document.Id,
                new IngestedDocumentPage(blockNumber, normalizedBlock),
                chunks,
                blockNumber,
                cancellationToken);

            nextChunkOrdinal += chunks.Count;
            chunkCount = nextChunkOrdinal;
            pageCount = blockNumber;
            DocumentIngestionCheckpoint savedCheckpoint = checkpoint with
            {
                NextBlock = blockNumber + 1,
                PageCount = pageCount,
                NextChunkOrdinal = nextChunkOrdinal
            };
            await saveProgressAsync(CreateProgress(document, file.Length, stream.Position, savedCheckpoint), cancellationToken);
        }

        if (pageCount == 0 || chunkCount == 0)
        {
            throw new InvalidOperationException("Il documento testuale non contiene testo estraibile.");
        }

        return new DocumentIngestionResult(pageCount, chunkCount);
    }

    private static async Task<string?> ReadNextTextBlockAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        StringBuilder builder = new();

        while (true)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(line);
            if (builder.Length >= TextBlockTargetCharacters && string.IsNullOrWhiteSpace(line))
            {
                break;
            }

            if (builder.Length >= TextBlockTargetCharacters * 2)
            {
                break;
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static DocumentIngestionProgress CreateProgress(
        ImportedDocument document,
        long fileLength,
        long streamPosition,
        DocumentIngestionCheckpoint checkpoint)
    {
        int progress = fileLength <= 0
            ? 0
            : Math.Clamp((int)Math.Round(streamPosition * 100d / fileLength), 0, 99);
        return new DocumentIngestionProgress(
            progress,
            $"Blocco testo {checkpoint.NextBlock - 1}",
            checkpoint with { Mode = document.FileExtension ?? checkpoint.Mode });
    }
}
