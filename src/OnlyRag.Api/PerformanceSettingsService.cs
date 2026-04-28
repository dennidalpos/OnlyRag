using System.Globalization;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Api;

internal sealed class PerformanceSettingsService : IPerformanceSettingsService
{
    public const int DefaultMaxParallelJobs = 1;
    public const int DefaultMaxOcrParallelPages = 1;
    public const int DefaultEmbeddingBatchSize = 1;
    public const int DefaultTranslationBatchSize = 1;
    public const int DefaultMaxContextChunks = 8;
    public const int DefaultRequestTimeoutSeconds = 120;

    private const int MinParallelism = 1;
    private const int MaxParallelJobsLimit = 4;
    private const int MaxOcrParallelPagesLimit = 4;
    private const int MaxEmbeddingBatchSize = 8;
    private const int MaxTranslationBatchSize = 4;
    private const int MinMaxContextChunks = 1;
    private const int MaxMaxContextChunks = 24;

    private const string MaxParallelJobsKey = "performance.maxParallelJobs";
    private const string MaxOcrParallelPagesKey = "performance.maxOcrParallelPages";
    private const string TranslationBatchSizeKey = "performance.translationBatchSize";
    public const string MaxContextChunksKey = "performance.maxContextChunks";
    private const string EnableLowResourceModeKey = "performance.enableLowResourceMode";
    private const string RequestTimeoutSecondsKey = "ollama.requestTimeoutSeconds";
    private const string EmbeddingBatchSizeKey = "ollama.embeddingBatchSize";

    private readonly ISettingsRepository settingsRepository;

    public PerformanceSettingsService(ISettingsRepository settingsRepository)
    {
        this.settingsRepository = settingsRepository;
    }

    public async Task<PerformanceSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        PerformanceSettings stored = new(
            await ReadIntAsync(MaxParallelJobsKey, DefaultMaxParallelJobs, MinParallelism, MaxParallelJobsLimit, cancellationToken),
            await ReadIntAsync(MaxOcrParallelPagesKey, DefaultMaxOcrParallelPages, MinParallelism, MaxOcrParallelPagesLimit, cancellationToken),
            await ReadIntAsync(EmbeddingBatchSizeKey, DefaultEmbeddingBatchSize, MinParallelism, MaxEmbeddingBatchSize, cancellationToken),
            await ReadIntAsync(TranslationBatchSizeKey, DefaultTranslationBatchSize, MinParallelism, MaxTranslationBatchSize, cancellationToken),
            await ReadIntAsync(MaxContextChunksKey, DefaultMaxContextChunks, MinMaxContextChunks, MaxMaxContextChunks, cancellationToken),
            await ReadIntAsync(RequestTimeoutSecondsKey, DefaultRequestTimeoutSeconds, 5, 600, cancellationToken),
            await ReadBoolAsync(EnableLowResourceModeKey, cancellationToken));

        return ApplyLowResourceMode(stored);
    }

    public async Task<PerformanceSettings> UpdateAsync(
        PerformanceSettings settings,
        CancellationToken cancellationToken = default)
    {
        PerformanceSettings normalized = Normalize(settings);

        await settingsRepository.UpsertAsync(MaxParallelJobsKey, normalized.MaxParallelJobs.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await settingsRepository.UpsertAsync(MaxOcrParallelPagesKey, normalized.MaxOcrParallelPages.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await settingsRepository.UpsertAsync(EmbeddingBatchSizeKey, normalized.EmbeddingBatchSize.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await settingsRepository.UpsertAsync(TranslationBatchSizeKey, normalized.TranslationBatchSize.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await settingsRepository.UpsertAsync(MaxContextChunksKey, normalized.MaxContextChunks.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await settingsRepository.UpsertAsync(RequestTimeoutSecondsKey, normalized.RequestTimeoutSeconds.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await settingsRepository.UpsertAsync(EnableLowResourceModeKey, normalized.EnableLowResourceMode ? "true" : "false", cancellationToken);

        return ApplyLowResourceMode(normalized);
    }

    internal static PerformanceSettings Normalize(PerformanceSettings settings)
    {
        PerformanceSettings normalized = settings with
        {
            MaxParallelJobs = Math.Clamp(settings.MaxParallelJobs, MinParallelism, MaxParallelJobsLimit),
            MaxOcrParallelPages = Math.Clamp(settings.MaxOcrParallelPages, MinParallelism, MaxOcrParallelPagesLimit),
            EmbeddingBatchSize = Math.Clamp(settings.EmbeddingBatchSize, MinParallelism, MaxEmbeddingBatchSize),
            TranslationBatchSize = Math.Clamp(settings.TranslationBatchSize, MinParallelism, MaxTranslationBatchSize),
            MaxContextChunks = Math.Clamp(settings.MaxContextChunks, MinMaxContextChunks, MaxMaxContextChunks),
            RequestTimeoutSeconds = OllamaSettingsService.ValidateRequestTimeoutSeconds(settings.RequestTimeoutSeconds)
        };

        return ApplyLowResourceMode(normalized);
    }

    private static PerformanceSettings ApplyLowResourceMode(PerformanceSettings settings)
    {
        if (!settings.EnableLowResourceMode)
        {
            return settings;
        }

        return settings with
        {
            MaxParallelJobs = 1,
            MaxOcrParallelPages = 1,
            EmbeddingBatchSize = 1,
            TranslationBatchSize = 1,
            MaxContextChunks = Math.Min(settings.MaxContextChunks, DefaultMaxContextChunks)
        };
    }

    private async Task<int> ReadIntAsync(
        string key,
        int defaultValue,
        int min,
        int max,
        CancellationToken cancellationToken)
    {
        string? value = await settingsRepository.GetValueAsync(key, cancellationToken);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? Math.Clamp(parsed, min, max)
            : defaultValue;
    }

    private async Task<bool> ReadBoolAsync(string key, CancellationToken cancellationToken)
    {
        string? value = await settingsRepository.GetValueAsync(key, cancellationToken);
        return bool.TryParse(value, out bool parsed) && parsed;
    }
}
