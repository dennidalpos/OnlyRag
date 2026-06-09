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
    private const string ProfileKey = "performance.profile";
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
        bool lowResourceMode = await ReadBoolAsync(EnableLowResourceModeKey, cancellationToken);
        string profile = NormalizeProfile(await settingsRepository.GetValueAsync(ProfileKey, cancellationToken));
        if (lowResourceMode && profile == PerformanceProfileNames.Auto)
        {
            profile = PerformanceProfileNames.Eco;
        }

        PerformanceSettings stored = new(
            await ReadIntAsync(MaxParallelJobsKey, DefaultMaxParallelJobs, MinParallelism, MaxParallelJobsLimit, cancellationToken),
            await ReadIntAsync(MaxOcrParallelPagesKey, DefaultMaxOcrParallelPages, MinParallelism, MaxOcrParallelPagesLimit, cancellationToken),
            await ReadIntAsync(EmbeddingBatchSizeKey, DefaultEmbeddingBatchSize, MinParallelism, MaxEmbeddingBatchSize, cancellationToken),
            await ReadIntAsync(TranslationBatchSizeKey, DefaultTranslationBatchSize, MinParallelism, MaxTranslationBatchSize, cancellationToken),
            await ReadIntAsync(MaxContextChunksKey, DefaultMaxContextChunks, MinMaxContextChunks, MaxMaxContextChunks, cancellationToken),
            await ReadIntAsync(RequestTimeoutSecondsKey, DefaultRequestTimeoutSeconds, 5, 600, cancellationToken),
            lowResourceMode,
            profile);

        return await ApplyProfileAsync(stored, cancellationToken);
    }

    public async Task<PerformanceSettings> UpdateAsync(
        PerformanceSettings settings,
        CancellationToken cancellationToken = default)
    {
        PerformanceSettings normalized = await NormalizeAsync(settings, cancellationToken);

        await settingsRepository.UpsertAsync(MaxParallelJobsKey, normalized.MaxParallelJobs.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await settingsRepository.UpsertAsync(MaxOcrParallelPagesKey, normalized.MaxOcrParallelPages.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await settingsRepository.UpsertAsync(EmbeddingBatchSizeKey, normalized.EmbeddingBatchSize.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await settingsRepository.UpsertAsync(TranslationBatchSizeKey, normalized.TranslationBatchSize.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await settingsRepository.UpsertAsync(MaxContextChunksKey, normalized.MaxContextChunks.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await settingsRepository.UpsertAsync(RequestTimeoutSecondsKey, normalized.RequestTimeoutSeconds.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await settingsRepository.UpsertAsync(EnableLowResourceModeKey, normalized.EnableLowResourceMode ? "true" : "false", cancellationToken);
        await settingsRepository.UpsertAsync(ProfileKey, normalized.Profile, cancellationToken);

        return normalized;
    }

    internal static PerformanceSettings Normalize(PerformanceSettings settings)
    {
        string profile = NormalizeProfile(settings.Profile);
        if (settings.EnableLowResourceMode && profile == PerformanceProfileNames.Auto)
        {
            profile = PerformanceProfileNames.Eco;
        }

        PerformanceSettings normalized = settings with
        {
            MaxParallelJobs = Math.Clamp(settings.MaxParallelJobs, MinParallelism, MaxParallelJobsLimit),
            MaxOcrParallelPages = Math.Clamp(settings.MaxOcrParallelPages, MinParallelism, MaxOcrParallelPagesLimit),
            EmbeddingBatchSize = Math.Clamp(settings.EmbeddingBatchSize, MinParallelism, MaxEmbeddingBatchSize),
            TranslationBatchSize = Math.Clamp(settings.TranslationBatchSize, MinParallelism, MaxTranslationBatchSize),
            MaxContextChunks = Math.Clamp(settings.MaxContextChunks, MinMaxContextChunks, MaxMaxContextChunks),
            RequestTimeoutSeconds = OllamaSettingsService.ValidateRequestTimeoutSeconds(settings.RequestTimeoutSeconds),
            Profile = profile,
            EffectiveProfile = profile,
            EnableLowResourceMode = profile == PerformanceProfileNames.Eco
        };

        return profile switch
        {
            PerformanceProfileNames.Eco => ApplyPreset(normalized, PerformanceProfileNames.Eco),
            PerformanceProfileNames.Balanced => ApplyPreset(normalized, PerformanceProfileNames.Balanced),
            PerformanceProfileNames.Power => ApplyPreset(normalized, PerformanceProfileNames.Power),
            PerformanceProfileNames.Custom => normalized with
            {
                EffectiveProfile = PerformanceProfileNames.Custom,
                EnableLowResourceMode = false
            },
            _ => normalized with
            {
                Profile = PerformanceProfileNames.Auto,
                EffectiveProfile = PerformanceProfileNames.Auto,
                EnableLowResourceMode = false
            }
        };
    }

    private async Task<PerformanceSettings> NormalizeAsync(
        PerformanceSettings settings,
        CancellationToken cancellationToken)
    {
        PerformanceSettings normalized = Normalize(settings);
        if (normalized.Profile != PerformanceProfileNames.Auto)
        {
            return normalized;
        }

        string effectiveProfile = await ResolveAutoProfileAsync(cancellationToken);
        return ApplyPreset(normalized, effectiveProfile) with
        {
            Profile = PerformanceProfileNames.Auto
        };
    }

    private Task<PerformanceSettings> ApplyProfileAsync(
        PerformanceSettings settings,
        CancellationToken cancellationToken) =>
        NormalizeAsync(settings, cancellationToken);

    private async Task<string> ResolveAutoProfileAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await Task.CompletedTask;
            double totalGiB = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024d / 1024d / 1024d;
            if ((totalGiB > 0d && totalGiB < 8d) || Environment.ProcessorCount <= 4)
            {
                return PerformanceProfileNames.Eco;
            }

            if (totalGiB >= 16d && Environment.ProcessorCount >= 8)
            {
                return PerformanceProfileNames.Power;
            }

            return PerformanceProfileNames.Balanced;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return PerformanceProfileNames.Balanced;
        }
    }

    private static PerformanceSettings ApplyPreset(PerformanceSettings settings, string profile) =>
        profile switch
        {
            PerformanceProfileNames.Eco => settings with
            {
                MaxParallelJobs = 1,
                MaxOcrParallelPages = 1,
                EmbeddingBatchSize = 1,
                TranslationBatchSize = 1,
                MaxContextChunks = 6,
                RequestTimeoutSeconds = 180,
                EnableLowResourceMode = true,
                EffectiveProfile = PerformanceProfileNames.Eco
            },
            PerformanceProfileNames.Power => settings with
            {
                MaxParallelJobs = 4,
                MaxOcrParallelPages = 4,
                EmbeddingBatchSize = 4,
                TranslationBatchSize = 2,
                MaxContextChunks = 12,
                RequestTimeoutSeconds = 120,
                EnableLowResourceMode = false,
                EffectiveProfile = PerformanceProfileNames.Power
            },
            _ => settings with
            {
                MaxParallelJobs = 2,
                MaxOcrParallelPages = 2,
                EmbeddingBatchSize = 2,
                TranslationBatchSize = 1,
                MaxContextChunks = 8,
                RequestTimeoutSeconds = 120,
                EnableLowResourceMode = false,
                EffectiveProfile = PerformanceProfileNames.Balanced
            }
        };

    private static string NormalizeProfile(string? value)
    {
        string normalized = (value ?? PerformanceProfileNames.Auto).Trim().ToLowerInvariant();
        return normalized switch
        {
            PerformanceProfileNames.Auto => PerformanceProfileNames.Auto,
            PerformanceProfileNames.Eco => PerformanceProfileNames.Eco,
            "bilanciato" or PerformanceProfileNames.Balanced => PerformanceProfileNames.Balanced,
            "potente" or PerformanceProfileNames.Power => PerformanceProfileNames.Power,
            "personalizzato" or PerformanceProfileNames.Custom => PerformanceProfileNames.Custom,
            _ => PerformanceProfileNames.Auto
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
