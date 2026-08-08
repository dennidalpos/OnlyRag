using OnlyRag.Core;
using OnlyRag.Infrastructure.Ocr;

namespace OnlyRag.Api;

internal sealed class DiagnosticsProbeCacheService
{
    private static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromSeconds(60);

    private readonly TimeProvider timeProvider;
    private readonly TimeSpan cacheDuration;
    private readonly SemaphoreSlim gate = new(1, 1);
    private CachedProbe<OcrEngineAvailability>? ocrAvailability;
    private CachedProbe<OcrGpuCapabilityResponse>? ocrGpuCapability;
    private CachedProbe<SystemTelemetryResponse>? systemTelemetry;

    public DiagnosticsProbeCacheService()
        : this(TimeProvider.System, DefaultCacheDuration)
    {
    }

    internal DiagnosticsProbeCacheService(TimeProvider timeProvider, TimeSpan cacheDuration)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (cacheDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(cacheDuration), "Diagnostics cache duration must be positive.");
        }

        this.timeProvider = timeProvider;
        this.cacheDuration = cacheDuration;
    }

    public Task<OcrEngineAvailability> CheckOcrAvailabilityAsync(
        IOcrEngine ocrEngine,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ocrEngine);

        return GetCachedAsync(
            () => ocrAvailability,
            value => ocrAvailability = value,
            () => ocrEngine.CheckAvailabilityAsync(cancellationToken),
            cancellationToken);
    }

    public Task<OcrGpuCapabilityResponse> CheckOcrGpuCapabilityAsync(
        OcrGpuCapabilityService ocrGpuCapabilityService,
        IOcrEngine ocrEngine,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ocrGpuCapabilityService);
        ArgumentNullException.ThrowIfNull(ocrEngine);

        return GetCachedAsync(
            () => ocrGpuCapability,
            value => ocrGpuCapability = value,
            () => ocrGpuCapabilityService.CheckAsync(ocrEngine, cancellationToken),
            cancellationToken);
    }

    public Task<SystemTelemetryResponse> CaptureSystemTelemetryAsync(
        SystemTelemetryService systemTelemetryService,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(systemTelemetryService);

        return GetCachedAsync(
            () => systemTelemetry,
            value => systemTelemetry = value,
            () => systemTelemetryService.CaptureAsync(cancellationToken),
            cancellationToken);
    }

    private async Task<T> GetCachedAsync<T>(
        Func<CachedProbe<T>?> read,
        Action<CachedProbe<T>> write,
        Func<Task<T>> refresh,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        CachedProbe<T>? current = read();
        if (current is CachedProbe<T> currentProbe && IsFresh(currentProbe, now))
        {
            return currentProbe.Value;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            now = timeProvider.GetUtcNow();
            current = read();
            if (current is CachedProbe<T> lockedProbe && IsFresh(lockedProbe, now))
            {
                return lockedProbe.Value;
            }

            T refreshed = await refresh();
            write(new CachedProbe<T>(timeProvider.GetUtcNow(), refreshed));
            return refreshed;
        }
        finally
        {
            gate.Release();
        }
    }

    private bool IsFresh<T>(CachedProbe<T> probe, DateTimeOffset now)
    {
        return now - probe.CapturedAtUtc < cacheDuration;
    }

    private readonly record struct CachedProbe<T>(DateTimeOffset CapturedAtUtc, T Value);
}
