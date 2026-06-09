using DocumentFormat.OpenXml.Packaging;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Ingestion;

public sealed partial class DocumentIngestionService
{
    private async Task<DocumentIngestionResult> IngestOfficeOpenXmlAsync(
        ImportedDocument document,
        DocumentIngestionCheckpoint checkpoint,
        DocumentIngestionOptions options,
        string extension,
        Func<DocumentIngestionProgress, CancellationToken, Task> saveProgressAsync,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(document.OriginalPath))
        {
            throw new FileNotFoundException("File originale documento non trovato.", document.OriginalPath);
        }

        IReadOnlyList<OfficeOpenXmlTextUnit> units;
        try
        {
            units = officeExtractor.Extract(document.OriginalPath, extension);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or FileFormatException or InvalidDataException or IOException)
        {
            throw new InvalidOperationException($"{DescribeOfficeFormat(extension)} non leggibile o non valido.", ex);
        }

        int totalUnits = units.Count;
        int nextUnit = Math.Max(1, checkpoint.NextBlock);
        int nextChunkOrdinal = Math.Max(0, checkpoint.NextChunkOrdinal);
        int chunkCount = nextChunkOrdinal;
        int processedUnitCount = Math.Max(0, checkpoint.PageCount);

        for (int unitIndex = nextUnit - 1; unitIndex < units.Count; unitIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OfficeOpenXmlTextUnit unit = units[unitIndex];
            string text = unit.Text.Trim();
            IReadOnlyList<IngestedDocumentChunk> chunks = string.IsNullOrWhiteSpace(text)
                ? []
                : chunker.CreateChunks(text, unit.UnitNumber, unit.UnitNumber, nextChunkOrdinal, options);

            await documents.SaveIngestedPageAsync(
                document.Id,
                new IngestedDocumentPage(unit.UnitNumber, text),
                chunks,
                totalUnits,
                cancellationToken);

            nextChunkOrdinal += chunks.Count;
            chunkCount = nextChunkOrdinal;
            processedUnitCount = unit.UnitNumber;
            DocumentIngestionCheckpoint savedCheckpoint = checkpoint with
            {
                NextBlock = unit.UnitNumber + 1,
                PageCount = totalUnits,
                NextChunkOrdinal = nextChunkOrdinal,
                Mode = extension
            };
            await saveProgressAsync(
                new DocumentIngestionProgress(
                    CalculateProgress(unit.UnitNumber, totalUnits),
                    $"{DescribeOfficeUnit(extension)} {unit.UnitNumber}/{totalUnits}",
                    savedCheckpoint),
                cancellationToken);
        }

        if (processedUnitCount == 0 || chunkCount == 0)
        {
            throw new InvalidOperationException($"{DescribeOfficeFormat(extension)} non contiene testo estraibile.");
        }

        return new DocumentIngestionResult(totalUnits, chunkCount);
    }

    private static string DescribeOfficeFormat(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".docx" => "DOCX",
            ".xlsx" => "XLSX",
            ".pptx" => "PPTX",
            _ => "Office Open XML"
        };
    }

    private static string DescribeOfficeUnit(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".docx" => "DOCX sezione logica",
            ".xlsx" => "XLSX foglio",
            ".pptx" => "PPTX slide",
            _ => "Unita documento"
        };
    }
}
