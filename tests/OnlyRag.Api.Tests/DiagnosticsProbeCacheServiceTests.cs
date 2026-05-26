using OnlyRag.Infrastructure.Ocr;

namespace OnlyRag.Api.Tests;

public sealed class DiagnosticsProbeCacheServiceTests
{
    [Fact]
    public async Task CheckOcrAvailabilityAsync_ReusesRecentProbe()
    {
        MutableTimeProvider timeProvider = new() { UtcNow = new DateTimeOffset(2026, 5, 26, 0, 0, 0, TimeSpan.Zero) };
        DiagnosticsProbeCacheService cache = new(timeProvider, TimeSpan.FromSeconds(10));
        CountingOcrEngine ocrEngine = new();

        OcrEngineAvailability first = await cache.CheckOcrAvailabilityAsync(ocrEngine);
        timeProvider.UtcNow = timeProvider.UtcNow.AddSeconds(5);
        OcrEngineAvailability second = await cache.CheckOcrAvailabilityAsync(ocrEngine);

        Assert.Equal(1, ocrEngine.CpuCheckCount);
        Assert.Equal(first.EngineVersion, second.EngineVersion);
    }

    [Fact]
    public async Task CheckOcrAvailabilityAsync_RefreshesExpiredProbe()
    {
        MutableTimeProvider timeProvider = new() { UtcNow = new DateTimeOffset(2026, 5, 26, 0, 0, 0, TimeSpan.Zero) };
        DiagnosticsProbeCacheService cache = new(timeProvider, TimeSpan.FromSeconds(10));
        CountingOcrEngine ocrEngine = new();

        OcrEngineAvailability first = await cache.CheckOcrAvailabilityAsync(ocrEngine);
        timeProvider.UtcNow = timeProvider.UtcNow.AddSeconds(11);
        OcrEngineAvailability second = await cache.CheckOcrAvailabilityAsync(ocrEngine);

        Assert.Equal(2, ocrEngine.CpuCheckCount);
        Assert.NotEqual(first.EngineVersion, second.EngineVersion);
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; }

        public override DateTimeOffset GetUtcNow()
        {
            return UtcNow;
        }
    }

    private sealed class CountingOcrEngine : IOcrEngine
    {
        public string EngineName => "Test OCR";

        public string EngineVersion => "test";

        public string PreprocessVersion => "test-preprocess";

        public int CpuCheckCount { get; private set; }

        public Task<OcrEngineAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
        {
            CpuCheckCount++;
            return Task.FromResult(new OcrEngineAvailability(
                true,
                EngineName,
                $"cpu-check-{CpuCheckCount}",
                Message: null));
        }

        public Task<OcrEngineAvailability> CheckAvailabilityAsync(
            string device,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new OcrEngineAvailability(
                true,
                EngineName,
                $"{device}-check",
                Message: null));
        }

        public Task<OcrPagePreparation> PreparePageAsync(
            OcrPagePreparationRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<OcrPageResult> RecognizeAsync(
            OcrRecognitionRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
