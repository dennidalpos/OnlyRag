using System.Text;
using System.Security.Cryptography;
using System.IO.Compression;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;
using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Ingestion;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Tests;

public sealed partial class DocumentIngestionServiceTests
{
    [Fact]
    public async Task IngestAsync_ZipIndexesSupportedEntriesWithArchiveProvenanceAndResumes()
    {
        using TempStorage tempStorage = await TempStorage.CreateInitializedAsync();
        string archivePath = Path.Combine(tempStorage.Paths.DocumentOriginalsDirectory, "bundle.zip");
        Directory.CreateDirectory(tempStorage.Paths.DocumentOriginalsDirectory);
        await using (FileStream stream = new(archivePath, FileMode.CreateNew))
        {
            using ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true);
            ZipArchiveEntry first = archive.CreateEntry("folder/first.txt");
            await using (StreamWriter writer = new(first.Open(), Encoding.UTF8))
            {
                await writer.WriteAsync("first archived fact");
            }

            ZipArchiveEntry unsupported = archive.CreateEntry("image.bin");
            await using (StreamWriter writer = new(unsupported.Open(), Encoding.UTF8))
            {
                await writer.WriteAsync("ignored");
            }

            ZipArchiveEntry second = archive.CreateEntry("second.md");
            await using (StreamWriter writer = new(second.Open(), Encoding.UTF8))
            {
                await writer.WriteAsync("second archived fact");
            }

            ZipArchiveEntry duplicate = archive.CreateEntry("folder/first.txt");
            await using (StreamWriter writer = new(duplicate.Open(), Encoding.UTF8))
            {
                await writer.WriteAsync("duplicate archived fact");
            }
        }

        ImportedDocument document = await tempStorage.CreateBinaryDocumentAsync(
            "bundle.zip",
            await File.ReadAllBytesAsync(archivePath));
        DocumentIngestionService service = tempStorage.CreateIngestionService();
        List<DocumentIngestionProgress> progress = [];

        DocumentIngestionResult result = await service.IngestAsync(
            document,
            checkpoint: null,
            (item, _) =>
            {
                progress.Add(item);
                return Task.CompletedTask;
            });

        Assert.Equal(2, result.PageCount);
        Assert.Equal(2, result.ChunkCount);
        IReadOnlyList<string> pages = await tempStorage.ReadPageTextsAsync(document.Id);
        Assert.Contains(pages, page => page.Contains("folder/first.txt", StringComparison.Ordinal));
        Assert.Contains(pages, page => page.Contains("second.md", StringComparison.Ordinal));
        Assert.DoesNotContain(pages, page => page.Contains("image.bin", StringComparison.Ordinal));
        Assert.Equal(4, progress.Count);
        Assert.Equal(5, progress[^1].Checkpoint.NextBlock);

        IReadOnlyList<ArchiveManifestEntry> manifest = await tempStorage.ArchiveManifest.ListAsync(document.Id);
        Assert.Equal(4, manifest.Count);
        Assert.Equal(
            [ArchiveManifestStatus.Indexed, ArchiveManifestStatus.Skipped, ArchiveManifestStatus.Indexed, ArchiveManifestStatus.Duplicate],
            manifest.Select(entry => entry.Status).ToArray());
        Assert.All(manifest, entry => Assert.False(string.IsNullOrWhiteSpace(entry.ContentSha256)));
        Assert.Equal(1, manifest[0].PageCount);
        Assert.Equal(1, manifest[2].ChunkCount);

        DocumentIngestionResult resumed = await service.IngestAsync(
            document,
            progress[1].Checkpoint,
            (_, _) => Task.CompletedTask);

        Assert.Equal(result.PageCount, resumed.PageCount);
        Assert.Equal(result.ChunkCount, resumed.ChunkCount);
        Assert.Equal(2, await tempStorage.ReadChunkCountAsync(document.Id));
        Assert.Equal(4, (await tempStorage.ArchiveManifest.ListAsync(document.Id)).Count);
    }

    [Fact]
    public void Chunker_PreservesOverlapBetweenChunks()
    {
        DocumentTextChunker chunker = new();
        string text = string.Join("\n\n", Enumerable.Range(0, 250).Select(index => $"word{index}"));

        IReadOnlyList<IngestedDocumentChunk> chunks = chunker.CreateChunks(
            text,
            pageStart: 1,
            pageEnd: 1,
            firstOrdinal: 0,
            DocumentIngestionOptions.Normalize(100, 20));

        Assert.True(chunks.Count >= 3);
        Assert.Equal(0, chunks[0].Ordinal);
        Assert.Equal(1, chunks[1].Ordinal);
        Assert.Contains("word80", chunks[1].Text, StringComparison.Ordinal);
        Assert.Equal(chunks[0].ContentHash.Length, chunks[1].ContentHash.Length);
    }

    [Fact]
    public async Task IngestAsync_TextFile_SavesPageChunksAndCheckpoint()
    {
        using TempStorage tempStorage = await TempStorage.CreateInitializedAsync();
        ImportedDocument document = await tempStorage.CreateDocumentAsync(
            "notes.txt",
            "First paragraph.\n\nSecond paragraph with enough text for ingestion.");
        DocumentIngestionService service = tempStorage.CreateIngestionService();
        List<DocumentIngestionProgress> progress = [];

        DocumentIngestionResult result = await service.IngestAsync(
            document,
            checkpoint: null,
            (item, _) =>
            {
                progress.Add(item);
                return Task.CompletedTask;
            });

        ImportedDocument? updated = await tempStorage.Documents.GetAsync(document.Id);
        Assert.Equal(1, result.PageCount);
        Assert.Equal(1, result.ChunkCount);
        Assert.NotNull(updated);
        Assert.Equal(1, updated.PageCount);
        Assert.Equal(1, updated.ChunkCount);
        Assert.Single(progress);
        Assert.Equal(2, progress[0].Checkpoint.NextBlock);
        Assert.Equal(1, progress[0].Checkpoint.NextChunkOrdinal);
    }

    [Fact]
    public async Task IngestAsync_LargeTextFile_ProcessesMultipleBlocks()
    {
        using TempStorage tempStorage = await TempStorage.CreateInitializedAsync();
        string largeBlock = string.Join(' ', Enumerable.Range(0, 15000).Select(index => $"alpha{index}"));
        ImportedDocument document = await tempStorage.CreateDocumentAsync(
            "large.txt",
            $"{largeBlock}\n\n{largeBlock}\n\n{largeBlock}");
        DocumentIngestionService service = tempStorage.CreateIngestionService();
        List<DocumentIngestionProgress> progress = [];

        DocumentIngestionResult result = await service.IngestAsync(
            document,
            checkpoint: null,
            (item, _) =>
            {
                progress.Add(item);
                return Task.CompletedTask;
            });

        ImportedDocument? updated = await tempStorage.Documents.GetAsync(document.Id);
        Assert.True(result.PageCount >= 2);
        Assert.True(result.ChunkCount > result.PageCount);
        Assert.NotNull(updated);
        Assert.Equal(result.PageCount, updated.PageCount);
        Assert.Equal(result.ChunkCount, updated.ChunkCount);
        Assert.Equal(result.PageCount, progress.Count);
    }

    [Fact]
    public async Task IngestAsync_TextFile_UsesStoredIngestionChunkingSettings()
    {
        using TempStorage tempStorage = await TempStorage.CreateInitializedAsync();
        IngestionSettingsStore settingsStore = new(tempStorage.Settings);
        await settingsStore.UpdateAsync(new IngestionSettings(100, 0));
        string text = string.Join(
            "\n\n",
            Enumerable.Range(0, 6).Select(block =>
                string.Join(' ', Enumerable.Range(0, 80).Select(index => $"chunk{block}_{index}"))));
        ImportedDocument document = await tempStorage.CreateDocumentAsync("chunked.txt", text);
        DocumentIngestionService service = tempStorage.CreateIngestionService();

        DocumentIngestionResult result = await service.IngestAsync(
            document,
            checkpoint: null,
            (_, _) => Task.CompletedTask);

        Assert.True(result.ChunkCount >= 3);
        Assert.Equal(result.ChunkCount, await tempStorage.ReadChunkCountAsync(document.Id));
    }

    [Fact]
    public async Task IngestAsync_MarkdownFile_SavesChunks()
    {
        using TempStorage tempStorage = await TempStorage.CreateInitializedAsync();
        ImportedDocument document = await tempStorage.CreateDocumentAsync(
            "readme.md",
            "# Title\n\n- Item one\n- Item two\n\nBody paragraph.");
        DocumentIngestionService service = tempStorage.CreateIngestionService();

        DocumentIngestionResult result = await service.IngestAsync(
            document,
            checkpoint: null,
            (_, _) => Task.CompletedTask);

        ImportedDocument? updated = await tempStorage.Documents.GetAsync(document.Id);
        Assert.Equal(1, result.PageCount);
        Assert.True(result.ChunkCount >= 1);
        Assert.NotNull(updated);
        Assert.Equal(".md", updated.FileExtension);
        Assert.Equal(result.ChunkCount, updated.ChunkCount);
    }

    [Fact]
    public async Task IngestAsync_CsvFile_SavesChunks()
    {
        using TempStorage tempStorage = await TempStorage.CreateInitializedAsync();
        ImportedDocument document = await tempStorage.CreateDocumentAsync(
            "data.csv",
            "id,name,value\n1,alpha,100\n2,beta,200\n3,gamma,300");
        DocumentIngestionService service = tempStorage.CreateIngestionService();

        DocumentIngestionResult result = await service.IngestAsync(
            document,
            checkpoint: null,
            (_, _) => Task.CompletedTask);

        ImportedDocument? updated = await tempStorage.Documents.GetAsync(document.Id);
        Assert.Equal(1, result.PageCount);
        Assert.True(result.ChunkCount >= 1);
        Assert.NotNull(updated);
        Assert.Equal(".csv", updated.FileExtension);
        Assert.Equal(result.ChunkCount, updated.ChunkCount);
    }

    [Fact]
    public async Task IngestAsync_Docx_SavesLogicalSectionsAndTableText()
    {
        using TempStorage tempStorage = await TempStorage.CreateInitializedAsync();
        ImportedDocument document = await tempStorage.CreateBinaryDocumentAsync(
            "report.docx",
            CreateDocxFixture());
        DocumentIngestionService service = tempStorage.CreateIngestionService();
        List<DocumentIngestionProgress> progress = [];

        DocumentIngestionResult result = await service.IngestAsync(
            document,
            checkpoint: null,
            (item, _) =>
            {
                progress.Add(item);
                return Task.CompletedTask;
            });

        IReadOnlyList<string> pages = await tempStorage.ReadPageTextsAsync(document.Id);
        string joinedText = string.Join("\n", pages);
        Assert.Equal(2, result.PageCount);
        Assert.Equal(2, pages.Count);
        Assert.Contains("# Executive Summary", joinedText, StringComparison.Ordinal);
        Assert.Contains("| Name | Value |", joinedText, StringComparison.Ordinal);
        IReadOnlyList<string> chunks = await tempStorage.ReadChunkTextsAsync(document.Id);
        Assert.Contains(chunks, chunk => chunk.Contains("| Name | Value |", StringComparison.Ordinal));
        Assert.Equal(2, progress.Count);
        Assert.Equal(3, progress[^1].Checkpoint.NextBlock);
    }

    [Fact]
    public async Task IngestAsync_Xlsx_SavesSheetsWithCellCoordinates()
    {
        using TempStorage tempStorage = await TempStorage.CreateInitializedAsync();
        ImportedDocument document = await tempStorage.CreateBinaryDocumentAsync(
            "workbook.xlsx",
            CreateXlsxFixture());
        DocumentIngestionService service = tempStorage.CreateIngestionService();
        List<DocumentIngestionProgress> progress = [];

        DocumentIngestionResult result = await service.IngestAsync(
            document,
            checkpoint: null,
            (item, _) =>
            {
                progress.Add(item);
                return Task.CompletedTask;
            });

        IReadOnlyList<string> pages = await tempStorage.ReadPageTextsAsync(document.Id);
        Assert.Equal(2, result.PageCount);
        Assert.Equal(2, pages.Count);
        Assert.Contains("Foglio: Data", pages[0], StringComparison.Ordinal);
        Assert.Contains("| Name | Score |", pages[0], StringComparison.Ordinal);
        Assert.Contains("| Alice | 42 |", pages[0], StringComparison.Ordinal);
        Assert.Contains("Foglio: Notes", pages[1], StringComparison.Ordinal);
        Assert.Equal(2, progress.Count);
    }

    [Fact]
    public async Task IngestAsync_Pptx_SavesSlidesTextboxesAndNotes()
    {
        using TempStorage tempStorage = await TempStorage.CreateInitializedAsync();
        ImportedDocument document = await tempStorage.CreateBinaryDocumentAsync(
            "deck.pptx",
            CreatePptxFixture());
        DocumentIngestionService service = tempStorage.CreateIngestionService();

        DocumentIngestionResult result = await service.IngestAsync(
            document,
            checkpoint: null,
            (_, _) => Task.CompletedTask);

        IReadOnlyList<string> pages = await tempStorage.ReadPageTextsAsync(document.Id);
        Assert.Equal(1, result.PageCount);
        Assert.Single(pages);
        Assert.Contains("# Slide 1", pages[0], StringComparison.Ordinal);
        Assert.Contains("- Slide title", pages[0], StringComparison.Ordinal);
        Assert.Contains("- Slide body", pages[0], StringComparison.Ordinal);
        Assert.Contains("- Speaker note", pages[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task IngestAsync_Docx_ResumesFromLogicalUnitCheckpoint()
    {
        using TempStorage tempStorage = await TempStorage.CreateInitializedAsync();
        ImportedDocument document = await tempStorage.CreateBinaryDocumentAsync(
            "resume.docx",
            CreateDocxFixture());
        DocumentIngestionService service = tempStorage.CreateIngestionService();
        DocumentIngestionCheckpoint? checkpoint = null;

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await service.IngestAsync(
                document,
                checkpoint: null,
                (item, _) =>
                {
                    checkpoint = item.Checkpoint;
                    throw new OperationCanceledException();
                });
        });

        Assert.NotNull(checkpoint);
        DocumentIngestionResult resumed = await service.IngestAsync(
            document,
            checkpoint,
            (_, _) => Task.CompletedTask);

        ImportedDocument? updated = await tempStorage.Documents.GetAsync(document.Id);
        Assert.NotNull(updated);
        Assert.Equal(2, resumed.PageCount);
        Assert.Equal(resumed.ChunkCount, updated.ChunkCount);
        Assert.Equal(2, await tempStorage.ReadChunkCountAsync(document.Id));
    }

    [Fact]
    public async Task IngestAsync_CorruptedDocx_ReturnsReadableError()
    {
        using TempStorage tempStorage = await TempStorage.CreateInitializedAsync();
        ImportedDocument document = await tempStorage.CreateBinaryDocumentAsync(
            "corrupt.docx",
            Encoding.UTF8.GetBytes("not an Open XML package"));
        DocumentIngestionService service = tempStorage.CreateIngestionService();

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.IngestAsync(document, checkpoint: null, (_, _) => Task.CompletedTask));

        Assert.Contains("DOCX", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("LibreOffice", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("unsupported.doc")]
    [InlineData("unsupported.xls")]
    [InlineData("unsupported.ppt")]
    public async Task IngestAsync_BinaryOfficeFormat_IsNotSupported(string fileName)
    {
        using TempStorage tempStorage = await TempStorage.CreateInitializedAsync();
        ImportedDocument document = await tempStorage.CreateBinaryDocumentAsync(
            fileName,
            Encoding.UTF8.GetBytes("binary-office-placeholder"));
        DocumentIngestionService service = tempStorage.CreateIngestionService();

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.IngestAsync(document, checkpoint: null, (_, _) => Task.CompletedTask));

        Assert.Contains("non supportato", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IngestAsync_TextualPdf_SavesPageAndChunk()
    {
        using TempStorage tempStorage = await TempStorage.CreateInitializedAsync();
        ImportedDocument document = await tempStorage.CreateBinaryDocumentAsync(
            "sample.pdf",
            CreateSinglePageTextPdf("Hello PDF text"));
        DocumentIngestionService service = tempStorage.CreateIngestionService();
        List<DocumentIngestionProgress> progress = [];

        DocumentIngestionResult result = await service.IngestAsync(
            document,
            checkpoint: null,
            (item, _) =>
            {
                progress.Add(item);
                return Task.CompletedTask;
            });

        ImportedDocument? updated = await tempStorage.Documents.GetAsync(document.Id);
        Assert.Equal(1, result.PageCount);
        Assert.Equal(1, result.ChunkCount);
        Assert.NotNull(updated);
        Assert.Equal(1, updated.PageCount);
        Assert.Equal(1, updated.ChunkCount);
        Assert.Single(progress);
        Assert.Equal(99, progress[0].ProgressPercent);
    }

    [Fact]
    public async Task IngestAsync_PdfWithoutExtractableText_ReturnsReadableError()
    {
        using TempStorage tempStorage = await TempStorage.CreateInitializedAsync();
        ImportedDocument document = await tempStorage.CreateBinaryDocumentAsync(
            "empty.pdf",
            CreateSinglePageTextPdf(string.Empty));
        DocumentIngestionService service = tempStorage.CreateIngestionService();

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.IngestAsync(document, checkpoint: null, (_, _) => Task.CompletedTask));

        Assert.Contains("Runtime OCR non installato", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IngestAsync_ResumesFromCheckpointWithoutDuplicatingChunks()
    {
        using TempStorage tempStorage = await TempStorage.CreateInitializedAsync();
        string largeBlock = string.Join(' ', Enumerable.Range(0, 15000).Select(index => $"resume{index}"));
        ImportedDocument document = await tempStorage.CreateDocumentAsync(
            "resume.txt",
            $"{largeBlock}\n\n{largeBlock}");
        DocumentIngestionService service = tempStorage.CreateIngestionService();
        DocumentIngestionCheckpoint? checkpoint = null;

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await service.IngestAsync(
                document,
                checkpoint: null,
                (item, _) =>
                {
                    checkpoint = item.Checkpoint;
                    throw new OperationCanceledException();
                });
        });

        Assert.NotNull(checkpoint);
        DocumentIngestionResult resumed = await service.IngestAsync(
            document,
            checkpoint,
            (_, _) => Task.CompletedTask);

        ImportedDocument? updated = await tempStorage.Documents.GetAsync(document.Id);
        Assert.NotNull(updated);
        Assert.Equal(2, resumed.PageCount);
        Assert.Equal(resumed.ChunkCount, updated.ChunkCount);
    }

    [Fact]
    public void Chunker_PreservesMarkdownTableStructureAsAtomicUnit()
    {
        DocumentTextChunker chunker = new();
        string tableText = "| Colonna 1 | Colonna 2 |\n|---|---|\n| Valore A | Valore B |\n| Valore C | Valore D |";
        string largeFiller = string.Join(' ', Enumerable.Range(0, 800).Select(i => $"word{i}"));
        string text = $"{largeFiller}\n\n{tableText}\n\n{largeFiller}";

        IReadOnlyList<IngestedDocumentChunk> chunks = chunker.CreateChunks(
            text,
            pageStart: 1,
            pageEnd: 1,
            firstOrdinal: 0,
            DocumentIngestionOptions.Normalize(200, 20));

        Assert.True(chunks.Count > 1);
        IngestedDocumentChunk tableChunk = chunks.First(c => c.Text.Contains("| Colonna 1 | Colonna 2 |", StringComparison.Ordinal));
        Assert.Contains("| Valore A | Valore B |", tableChunk.Text, StringComparison.Ordinal);
        Assert.Contains("| Valore C | Valore D |", tableChunk.Text, StringComparison.Ordinal);
    }
}
