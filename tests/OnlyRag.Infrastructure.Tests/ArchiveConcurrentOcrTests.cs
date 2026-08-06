using System.IO.Compression;
using System.Text;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ingestion;
using OnlyRag.Infrastructure.Ocr;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public sealed partial class DocumentIngestionServiceTests
{
    [Fact]
    public async Task IngestAsync_MultiImageArchive_ProcessesConcurrentlyWithCleanState()
    {
        using TempStorage tempStorage = await TempStorage.CreateInitializedAsync();
        string archivePath = Path.Combine(tempStorage.Paths.DocumentOriginalsDirectory, "multi-images.zip");
        Directory.CreateDirectory(tempStorage.Paths.DocumentOriginalsDirectory);

        await using (FileStream stream = new(archivePath, FileMode.CreateNew))
        {
            using ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true);
            for (int i = 1; i <= 5; i++)
            {
                ZipArchiveEntry entry = archive.CreateEntry($"image_{i}.png");
                await using Stream entryStream = entry.Open();
                byte[] fakePngBytes = Encoding.UTF8.GetBytes($"fake-png-content-{i}");
                await entryStream.WriteAsync(fakePngBytes);
            }
        }

        ImportedDocument document = await tempStorage.CreateBinaryDocumentAsync(
            "multi-images.zip",
            await File.ReadAllBytesAsync(archivePath));

        var mockOcr = new MockBatchOcrEngine();
        DocumentIngestionService service = tempStorage.CreateIngestionService(ocrEngine: mockOcr);

        List<DocumentIngestionProgress> progress = [];
        DocumentIngestionResult result = await service.IngestAsync(
            document,
            checkpoint: null,
            (p, token) => { progress.Add(p); return Task.CompletedTask; },
            forceOcr: false,
            ocrLanguage: "ita");

        Assert.Equal(5, result.PageCount);
        Assert.Equal(5, result.ChunkCount);
        Assert.Equal(5, mockOcr.RecognizeCount);
    }

    private sealed class MockBatchOcrEngine : IOcrEngine
    {
        public int RecognizeCount;
        public string EngineName => "MockOcr";
        public string EngineVersion => "1.0";
        public string PreprocessVersion => "v1";

        public Task<OcrEngineAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new OcrEngineAvailability(true, EngineName, EngineVersion, "OK"));

        public Task<OcrEngineAvailability> CheckAvailabilityAsync(string device, CancellationToken cancellationToken = default) =>
            CheckAvailabilityAsync(cancellationToken);

        public Task<OcrPagePreparation> PreparePageAsync(OcrPagePreparationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OcrPagePreparation(request.SourcePath, "hash-" + request.PageNumber, 100, 100));

        public Task<IReadOnlyList<OcrPagePreparation>> PreparePageBatchAsync(IReadOnlyList<OcrPagePreparationRequest> requests, int maxConcurrency = 4, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OcrPagePreparation>>(requests.Select(r => new OcrPagePreparation(r.SourcePath, "hash-" + r.PageNumber, 100, 100)).ToList());

        public Task<OcrPageResult> RecognizeAsync(OcrRecognitionRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref RecognizeCount);
            return Task.FromResult(new OcrPageResult("Mock OCR text content", [], 0.99, EngineName, EngineVersion, request.Language));
        }

        public Task<IReadOnlyList<OcrPageResult>> RecognizeBatchAsync(IReadOnlyList<OcrRecognitionRequest> requests, int maxConcurrency = 4, CancellationToken cancellationToken = default)
        {
            Interlocked.Add(ref RecognizeCount, requests.Count);
            var results = requests.Select(r => new OcrPageResult("Mock OCR text content", [], 0.99, EngineName, EngineVersion, r.Language)).ToList();
            return Task.FromResult<IReadOnlyList<OcrPageResult>>(results);
        }
    }
}
