using System.Security.Cryptography;
using System.Text;
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
            ArchiveManifest = new SqliteArchiveManifestRepository(ConnectionFactory);
        }

        public string Root { get; }

        public AppStoragePaths Paths { get; }

        public LocalSqliteStoreDescriptor Descriptor { get; }

        public LocalSqliteConnectionFactory ConnectionFactory { get; }

        public SqliteDocumentRepository Documents { get; }

        public SqliteSettingsRepository Settings { get; }

        public SqliteArchiveManifestRepository ArchiveManifest { get; }

        public static async Task<TempStorage> CreateInitializedAsync()
        {
            string root = Path.Combine(Path.GetTempPath(), "OnlyRag.Ingestion.Tests", Guid.NewGuid().ToString("N"));
            TempStorage storage = new(root);
            LocalSqliteSchemaInitializer initializer = new(storage.Descriptor, storage.ConnectionFactory);
            LocalSqliteStorageService service = new(storage.Descriptor, initializer);
            await service.InitializeAsync();
            return storage;
        }

        public DocumentIngestionService CreateIngestionService()
        {
            return new DocumentIngestionService(
                Documents,
                Settings,
                new DocumentTextChunker(),
                new OfficeOpenXmlTextExtractor(),
                archiveManifestRepository: ArchiveManifest);
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
