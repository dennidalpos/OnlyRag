using System.Text;
using System.Security.Cryptography;
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

public sealed class DocumentIngestionServiceTests
{
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
        Assert.Contains("Titolo: Executive Summary", joinedText, StringComparison.Ordinal);
        Assert.Contains("Tabella:", joinedText, StringComparison.Ordinal);
        Assert.Contains("Riga 1: Cella 1: Name | Cella 2: Value", joinedText, StringComparison.Ordinal);
        IReadOnlyList<string> chunks = await tempStorage.ReadChunkTextsAsync(document.Id);
        Assert.Contains(chunks, chunk => chunk.Contains("Riga 1: Cella 1: Name | Cella 2: Value", StringComparison.Ordinal));
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
        Assert.Contains("Riga 1: [A1] Name | [B1] Score", pages[0], StringComparison.Ordinal);
        Assert.Contains("Riga 2: [A2] Alice | [B2] 42", pages[0], StringComparison.Ordinal);
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
        Assert.Contains("Textbox 1: Slide title", pages[0], StringComparison.Ordinal);
        Assert.Contains("Textbox 2: Slide body", pages[0], StringComparison.Ordinal);
        Assert.Contains("Note 1: Speaker note", pages[0], StringComparison.Ordinal);
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

        OfficeConversionUnavailableException error = await Assert.ThrowsAsync<OfficeConversionUnavailableException>(() =>
            service.IngestAsync(document, checkpoint: null, (_, _) => Task.CompletedTask));

        Assert.Contains("LibreOffice", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IngestAsync_LegacyDoc_UsesOfficeConverterAndCleansTemporaryDirectory()
    {
        using TempStorage tempStorage = await TempStorage.CreateInitializedAsync();
        ImportedDocument document = await tempStorage.CreateBinaryDocumentAsync(
            "legacy.doc",
            Encoding.UTF8.GetBytes("legacy-office-placeholder"));
        FakeOfficeConverter converter = new(tempStorage.Root, CreateSinglePageTextPdf("Converted legacy text"));
        DocumentIngestionService service = tempStorage.CreateIngestionService(converter);

        DocumentIngestionResult result = await service.IngestAsync(
            document,
            checkpoint: null,
            (_, _) => Task.CompletedTask);

        IReadOnlyList<string> pages = await tempStorage.ReadPageTextsAsync(document.Id);
        Assert.Equal(1, result.PageCount);
        Assert.Single(pages);
        Assert.Contains("Converted legacy text", pages[0], StringComparison.Ordinal);
        Assert.NotNull(converter.LastTemporaryDirectory);
        Assert.False(Directory.Exists(converter.LastTemporaryDirectory));
    }

    [Fact]
    public async Task IngestAsync_LegacyXls_WhenConverterUnavailableReturnsAdditionalComponentError()
    {
        using TempStorage tempStorage = await TempStorage.CreateInitializedAsync();
        ImportedDocument document = await tempStorage.CreateBinaryDocumentAsync(
            "legacy.xls",
            Encoding.UTF8.GetBytes("legacy-office-placeholder"));
        DocumentIngestionService service = tempStorage.CreateIngestionService(new UnavailableOfficeConversionService());

        OfficeConversionUnavailableException error = await Assert.ThrowsAsync<OfficeConversionUnavailableException>(() =>
            service.IngestAsync(document, checkpoint: null, (_, _) => Task.CompletedTask));

        Assert.Contains("LibreOffice", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IngestAsync_LegacyPpt_WhenConverterTimesOutReturnsReadableError()
    {
        using TempStorage tempStorage = await TempStorage.CreateInitializedAsync();
        ImportedDocument document = await tempStorage.CreateBinaryDocumentAsync(
            "legacy.ppt",
            Encoding.UTF8.GetBytes("legacy-office-placeholder"));
        DocumentIngestionService service = tempStorage.CreateIngestionService(
            new ThrowingOfficeConverter(new OfficeConversionException("Timeout conversione Office dopo 1 secondi.")));

        OfficeConversionException error = await Assert.ThrowsAsync<OfficeConversionException>(() =>
            service.IngestAsync(document, checkpoint: null, (_, _) => Task.CompletedTask));

        Assert.Contains("Timeout conversione Office", error.Message, StringComparison.Ordinal);
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

        Assert.Contains("PaddleOCR non configurato", error.Message, StringComparison.Ordinal);
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

    private sealed class TempStorage : IDisposable
    {
        private TempStorage(string root)
        {
            Root = root;
            Paths = AppStoragePaths.FromRoot(root);
            Descriptor = new LocalSqliteStoreDescriptor(Paths);
            ConnectionFactory = new LocalSqliteConnectionFactory(Descriptor);
            Documents = new SqliteDocumentRepository(ConnectionFactory);
            Settings = new SqliteSettingsRepository(ConnectionFactory);
        }

        public string Root { get; }

        public AppStoragePaths Paths { get; }

        public LocalSqliteStoreDescriptor Descriptor { get; }

        public LocalSqliteConnectionFactory ConnectionFactory { get; }

        public SqliteDocumentRepository Documents { get; }

        public SqliteSettingsRepository Settings { get; }

        public static async Task<TempStorage> CreateInitializedAsync()
        {
            string root = Path.Combine(Path.GetTempPath(), "OnlyRag.Ingestion.Tests", Guid.NewGuid().ToString("N"));
            TempStorage storage = new(root);
            LocalSqliteMigrator migrator = new(storage.Descriptor, storage.ConnectionFactory);
            LocalSqliteStorageService service = new(storage.Descriptor, migrator);
            await service.InitializeAsync();
            return storage;
        }

        public DocumentIngestionService CreateIngestionService(IOfficeConversionService? officeConverter = null)
        {
            return new DocumentIngestionService(
                Documents,
                Settings,
                new DocumentTextChunker(),
                new OfficeOpenXmlTextExtractor(),
                officeConversion: officeConverter);
        }

        public async Task<ImportedDocument> CreateDocumentAsync(string fileName, string content)
        {
            Directory.CreateDirectory(Paths.DocumentOriginalsDirectory);
            string path = Path.Combine(Paths.DocumentOriginalsDirectory, fileName);
            await File.WriteAllTextAsync(path, content, Encoding.UTF8);
            return await CreateDocumentRecordAsync(fileName, path, await File.ReadAllBytesAsync(path));
        }

        public async Task<ImportedDocument> CreateBinaryDocumentAsync(string fileName, byte[] content)
        {
            Directory.CreateDirectory(Paths.DocumentOriginalsDirectory);
            string path = Path.Combine(Paths.DocumentOriginalsDirectory, fileName);
            await File.WriteAllBytesAsync(path, content);
            return await CreateDocumentRecordAsync(fileName, path, content);
        }

        private async Task<ImportedDocument> CreateDocumentRecordAsync(string fileName, string path, byte[] content)
        {
            string extension = Path.GetExtension(fileName);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return await Documents.CreateAsync(
                new CreateDocumentRecordRequest(
                    Guid.NewGuid().ToString("N"),
                    fileName,
                    path,
                    Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
                    MimeType: DetectMimeType(extension),
                    FileExtension: extension,
                    new FileInfo(path).Length,
                    DocumentStatus.Imported,
                    PageCount: 0,
                    CurrentJobId: null,
                    LastError: null,
                    now,
                    now));
        }

        private static string DetectMimeType(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".md" => "text/markdown",
                ".pdf" => "application/pdf",
                _ => "text/plain"
            };
        }

        public async Task<IReadOnlyList<string>> ReadPageTextsAsync(long documentId)
        {
            await using Microsoft.Data.Sqlite.SqliteConnection connection = await ConnectionFactory.OpenConnectionAsync();
            await using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT text_content
                FROM document_pages
                WHERE document_id = $documentId
                ORDER BY page_number;
                """;
            command.Parameters.AddWithValue("$documentId", documentId);

            List<string> pages = [];
            await using Microsoft.Data.Sqlite.SqliteDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                pages.Add(reader.IsDBNull(0) ? string.Empty : reader.GetString(0));
            }

            return pages;
        }

        public async Task<int> ReadChunkCountAsync(long documentId)
        {
            await using Microsoft.Data.Sqlite.SqliteConnection connection = await ConnectionFactory.OpenConnectionAsync();
            await using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM chunks WHERE document_id = $documentId;";
            command.Parameters.AddWithValue("$documentId", documentId);
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        public async Task<IReadOnlyList<string>> ReadChunkTextsAsync(long documentId)
        {
            await using Microsoft.Data.Sqlite.SqliteConnection connection = await ConnectionFactory.OpenConnectionAsync();
            await using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT content
                FROM chunks
                WHERE document_id = $documentId
                ORDER BY chunk_index;
                """;
            command.Parameters.AddWithValue("$documentId", documentId);

            List<string> chunks = [];
            await using Microsoft.Data.Sqlite.SqliteDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                chunks.Add(reader.GetString(0));
            }

            return chunks;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private static byte[] CreateSinglePageTextPdf(string text)
    {
        string escapedText = text.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("(", @"\(", StringComparison.Ordinal)
            .Replace(")", @"\)", StringComparison.Ordinal);
        string stream = $"BT /F1 24 Tf 100 700 Td ({escapedText}) Tj ET";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}\nendstream"
        ];

        StringBuilder builder = new();
        List<int> offsets = [];
        builder.Append("%PDF-1.4\n");
        for (int index = 0; index < objects.Length; index++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(index + 1).Append(" 0 obj\n")
                .Append(objects[index]).Append("\nendobj\n");
        }

        int xrefOffset = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append("xref\n0 ").Append(objects.Length + 1).Append('\n')
            .Append("0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            builder.Append(offset.ToString("D10")).Append(" 00000 n \n");
        }

        builder.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\n")
            .Append("startxref\n").Append(xrefOffset).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private sealed class FakeOfficeConverter : IOfficeConversionService
    {
        private readonly string root;
        private readonly byte[] pdfBytes;

        public FakeOfficeConverter(string root, byte[] pdfBytes)
        {
            this.root = root;
            this.pdfBytes = pdfBytes;
        }

        public string? LastTemporaryDirectory { get; private set; }

        public Task<OfficeConverterAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new OfficeConverterAvailability(true, "fake-soffice.exe", "Fake converter available.", null));
        }

        public async Task<OfficeConversionResult> ConvertToPdfAsync(
            OfficeConversionRequest request,
            CancellationToken cancellationToken = default)
        {
            LastTemporaryDirectory = Path.Combine(root, "temp", "office-conversion", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(LastTemporaryDirectory);
            string pdfPath = Path.Combine(LastTemporaryDirectory, Path.ChangeExtension(request.OriginalFileName, ".pdf"));
            await File.WriteAllBytesAsync(pdfPath, pdfBytes, cancellationToken);
            return new OfficeConversionResult(pdfPath, LastTemporaryDirectory);
        }
    }

    private sealed class ThrowingOfficeConverter : IOfficeConversionService
    {
        private readonly Exception exception;

        public ThrowingOfficeConverter(Exception exception)
        {
            this.exception = exception;
        }

        public Task<OfficeConverterAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new OfficeConverterAvailability(true, "fake-soffice.exe", "Fake converter available.", null));
        }

        public Task<OfficeConversionResult> ConvertToPdfAsync(
            OfficeConversionRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<OfficeConversionResult>(exception);
        }
    }

    private static byte[] CreateDocxFixture()
    {
        using MemoryStream stream = new();
        using (WordprocessingDocument document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            MainDocumentPart mainPart = document.AddMainDocumentPart();
            mainPart.Document = new W.Document(new W.Body(
                CreateWordParagraph("Executive Summary", "Heading1"),
                CreateWordParagraph("First body paragraph."),
                new W.Table(
                    new W.TableRow(
                        new W.TableCell(CreateWordParagraph("Name")),
                        new W.TableCell(CreateWordParagraph("Value")))),
                CreateWordParagraph("Details", "Heading1"),
                CreateWordParagraph("Second section paragraph.")));
        }

        return stream.ToArray();
    }

    private static W.Paragraph CreateWordParagraph(string text, string? styleId = null)
    {
        W.Paragraph paragraph = new(new W.Run(new W.Text(text)));
        if (styleId is not null)
        {
            paragraph.ParagraphProperties = new W.ParagraphProperties(
                new W.ParagraphStyleId { Val = styleId });
        }

        return paragraph;
    }

    private static byte[] CreateXlsxFixture()
    {
        using MemoryStream stream = new();
        using (SpreadsheetDocument document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            WorkbookPart workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new S.Workbook();
            S.Sheets sheets = workbookPart.Workbook.AppendChild(new S.Sheets());
            AddWorksheet(workbookPart, sheets, "Data", 1, [
                [("A1", "Name"), ("B1", "Score")],
                [("A2", "Alice"), ("B2", "42")]
            ]);
            AddWorksheet(workbookPart, sheets, "Notes", 2, [
                [("A1", "Ready")]
            ]);
        }

        return stream.ToArray();
    }

    private static void AddWorksheet(
        WorkbookPart workbookPart,
        S.Sheets sheets,
        string name,
        uint sheetId,
        IReadOnlyList<IReadOnlyList<(string Reference, string Value)>> rows)
    {
        WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        S.SheetData sheetData = new();
        uint rowIndex = 0;
        foreach (IReadOnlyList<(string Reference, string Value)> rowCells in rows)
        {
            rowIndex++;
            S.Row row = new() { RowIndex = rowIndex };
            foreach ((string reference, string value) in rowCells)
            {
                row.Append(new S.Cell
                {
                    CellReference = reference,
                    DataType = S.CellValues.InlineString,
                    InlineString = new S.InlineString(new S.Text(value))
                });
            }

            sheetData.Append(row);
        }

        worksheetPart.Worksheet = new S.Worksheet(sheetData);
        sheets.Append(new S.Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = sheetId,
            Name = name
        });
    }

    private static byte[] CreatePptxFixture()
    {
        using MemoryStream stream = new();
        using (PresentationDocument document = PresentationDocument.Create(stream, PresentationDocumentType.Presentation))
        {
            PresentationPart presentationPart = document.AddPresentationPart();
            presentationPart.Presentation = new P.Presentation();
            SlidePart slidePart = presentationPart.AddNewPart<SlidePart>();
            slidePart.Slide = new P.Slide(new P.CommonSlideData(
                new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(new A.TransformGroup()),
                    CreatePresentationShape(2U, "Title", "Slide title"),
                    CreatePresentationShape(3U, "Body", "Slide body"))));

            NotesSlidePart notesPart = slidePart.AddNewPart<NotesSlidePart>();
            notesPart.NotesSlide = new P.NotesSlide(new P.CommonSlideData(
                new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(new A.TransformGroup()),
                    CreatePresentationShape(2U, "Notes", "Speaker note"))));

            string slideRelationshipId = presentationPart.GetIdOfPart(slidePart);
            presentationPart.Presentation.Append(new P.SlideIdList(
                new P.SlideId { Id = 256U, RelationshipId = slideRelationshipId }));
        }

        return stream.ToArray();
    }

    private static P.Shape CreatePresentationShape(uint id, string name, string text)
    {
        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name },
                new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(),
            new P.TextBody(
                new A.BodyProperties(),
                new A.ListStyle(),
                new A.Paragraph(new A.Run(new A.Text(text)))));
    }
}
