using System.Security.Cryptography;
using System.Text;
using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Ingestion;
using OnlyRag.Infrastructure.Ocr;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Tests;

public sealed partial class OcrPipelineTests
{
    private sealed class FakeOcrEngine : IOcrEngine
    {
        private readonly string text;
        private readonly double confidence;

        public FakeOcrEngine(string text, double confidence)
        {
            this.text = text;
            this.confidence = confidence;
        }

        public int PrepareCount { get; private set; }

        public int RecognizeCount { get; private set; }

        public string? LastLanguage { get; private set; }

        public OcrSettings? LastSettings { get; private set; }

        public OcrSettings? LastPreparationSettings { get; private set; }

        public string EngineName => "FakeOCR";

        public string EngineVersion => "1.0";

        public string PreprocessVersion => "fake-preprocess-v1";

        public Task<OcrEngineAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new OcrEngineAvailability(true, EngineName, EngineVersion, null));
        }

        public Task<OcrEngineAvailability> CheckAvailabilityAsync(
            string device,
            CancellationToken cancellationToken = default)
        {
            return CheckAvailabilityAsync(cancellationToken);
        }

        public async Task<OcrPagePreparation> PreparePageAsync(
            OcrPagePreparationRequest request,
            CancellationToken cancellationToken = default)
        {
            PrepareCount++;
            LastPreparationSettings = request.Settings;
            Directory.CreateDirectory(request.OutputDirectory);
            string output = Path.Combine(request.OutputDirectory, $"{Path.GetFileNameWithoutExtension(request.SourcePath)}-{request.PageNumber}.png");
            await File.WriteAllTextAsync(output, $"stable-prepared-page:{request.PageNumber}", cancellationToken);
            string hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(output, cancellationToken))).ToLowerInvariant();
            return new OcrPagePreparation(output, hash, 100, 100);
        }

        public async Task<IReadOnlyList<OcrPagePreparation>> PreparePageBatchAsync(
            IReadOnlyList<OcrPagePreparationRequest> requests,
            int maxConcurrency = 4,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(requests);
            if (requests.Count == 0)
            {
                return [];
            }

            int concurrency = Math.Clamp(maxConcurrency, 1, 16);
            using SemaphoreSlim semaphore = new(concurrency, concurrency);
            Task<OcrPagePreparation>[] tasks = requests.Select(async request =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    return await PreparePageAsync(request, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToArray();

            return await Task.WhenAll(tasks);
        }

        public Task<OcrPageResult> RecognizeAsync(
            OcrRecognitionRequest request,
            CancellationToken cancellationToken = default)
        {
            RecognizeCount++;
            LastLanguage = request.Language;
            LastSettings = request.Settings;
            return Task.FromResult(new OcrPageResult(text, [], confidence, EngineName, EngineVersion, request.Language));
        }

        public async Task<IReadOnlyList<OcrPageResult>> RecognizeBatchAsync(
            IReadOnlyList<OcrRecognitionRequest> requests,
            int maxConcurrency = 4,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(requests);
            if (requests.Count == 0)
            {
                return [];
            }

            int concurrency = Math.Clamp(maxConcurrency, 1, 16);
            using SemaphoreSlim semaphore = new(concurrency, concurrency);
            Task<OcrPageResult>[] tasks = requests.Select(async request =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    return await RecognizeAsync(request, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToArray();

            return await Task.WhenAll(tasks);
        }
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
            OcrCache = new SqliteOcrCacheRepository(ConnectionFactory);
        }

        public string Root { get; }

        public AppStoragePaths Paths { get; }

        public LocalSqliteStoreDescriptor Descriptor { get; }

        public LocalSqliteConnectionFactory ConnectionFactory { get; }

        public SqliteDocumentRepository Documents { get; }

        public SqliteSettingsRepository Settings { get; }

        public SqliteOcrCacheRepository OcrCache { get; }

        public static async Task<TempStorage> CreateInitializedAsync()
        {
            string root = Path.Combine(Path.GetTempPath(), "OnlyRag.Ocr.Tests", Guid.NewGuid().ToString("N"));
            TempStorage storage = new(root);
            LocalSqliteSchemaInitializer initializer = new(storage.Descriptor, storage.ConnectionFactory);
            LocalSqliteStorageService service = new(storage.Descriptor, initializer);
            await service.InitializeAsync();
            return storage;
        }

        public DocumentIngestionService CreateIngestionService(IOcrEngine engine)
        {
            return new DocumentIngestionService(
                Documents,
                Settings,
                new DocumentTextChunker(),
                new OfficeOpenXmlTextExtractor(),
                engine,
                OcrCache,
                new OcrRetryPolicy(),
                Descriptor);
        }

        public async Task<ImportedDocument> CreateBinaryDocumentAsync(string fileName, byte[] content)
        {
            Directory.CreateDirectory(Paths.DocumentOriginalsDirectory);
            string path = Path.Combine(Paths.DocumentOriginalsDirectory, fileName);
            await File.WriteAllBytesAsync(path, content);
            string extension = Path.GetExtension(fileName);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return await Documents.CreateAsync(new CreateDocumentRecordRequest(
                Guid.NewGuid().ToString("N"),
                fileName,
                path,
                Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
                DocumentFileTypeDetector.DetectMimeType(fileName),
                extension,
                content.Length,
                DocumentStatus.Imported,
                PageCount: 0,
                CurrentJobId: null,
                LastError: null,
                now,
                now));
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
        return CreateMultiPageTextPdf(text);
    }

    private static byte[] CreateMultiPageTextPdf(params string[] pageTexts)
    {
        string[] pageObjects = pageTexts.Select((_, index) => $"{4 + (index * 2)} 0 R").ToArray();
        List<string> objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(' ', pageObjects)}] /Count {pageTexts.Length} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        ];

        foreach ((string text, int index) in pageTexts.Select((text, index) => (text, index)))
        {
            int contentObjectNumber = 5 + (index * 2);
            string escapedText = text.Replace(@"\", @"\\", StringComparison.Ordinal)
                .Replace("(", @"\(", StringComparison.Ordinal)
                .Replace(")", @"\)", StringComparison.Ordinal);
            string stream = string.IsNullOrEmpty(text)
                ? string.Empty
                : $"BT /F1 24 Tf 100 700 Td ({escapedText}) Tj ET";
            objects.Add(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents {contentObjectNumber} 0 R >>");
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}\nendstream");
        }

        StringBuilder builder = new();
        List<int> offsets = [];
        builder.Append("%PDF-1.4\n");
        for (int index = 0; index < objects.Count; index++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(index + 1).Append(" 0 obj\n")
                .Append(objects[index]).Append("\nendobj\n");
        }

        int xrefOffset = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append("xref\n0 ").Append(objects.Count + 1).Append('\n')
            .Append("0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            builder.Append(offset.ToString("D10")).Append(" 00000 n \n");
        }

        builder.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\n")
            .Append("startxref\n").Append(xrefOffset).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }
}
