using System.Globalization;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Ingestion;

public sealed class IngestionSettingsStore
{
    private const string ChunkSizeSettingKey = "ingestion.chunkSizeTokens";
    private const string OverlapSettingKey = "ingestion.overlapTokens";
    private const string ArchiveMaxFileCountSettingKey = "ingestion.archive.maxFileCount";
    private const string ArchiveMaxTotalBytesSettingKey = "ingestion.archive.maxTotalUncompressedBytes";
    private const string ArchiveMaxFileBytesSettingKey = "ingestion.archive.maxFileUncompressedBytes";
    private const string ArchiveMaxDepthSettingKey = "ingestion.archive.maxDirectoryDepth";

    private readonly ISettingsRepository settingsRepository;

    public IngestionSettingsStore(ISettingsRepository settingsRepository)
    {
        this.settingsRepository = settingsRepository;
    }

    public async Task<IngestionSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        string? chunkSizeValue = await settingsRepository.GetValueAsync(ChunkSizeSettingKey, cancellationToken);
        string? overlapValue = await settingsRepository.GetValueAsync(OverlapSettingKey, cancellationToken);
        Task<string?> maxFileCountTask = settingsRepository.GetValueAsync(ArchiveMaxFileCountSettingKey, cancellationToken);
        Task<string?> maxTotalBytesTask = settingsRepository.GetValueAsync(ArchiveMaxTotalBytesSettingKey, cancellationToken);
        Task<string?> maxFileBytesTask = settingsRepository.GetValueAsync(ArchiveMaxFileBytesSettingKey, cancellationToken);
        Task<string?> maxDepthTask = settingsRepository.GetValueAsync(ArchiveMaxDepthSettingKey, cancellationToken);
        await Task.WhenAll(maxFileCountTask, maxTotalBytesTask, maxFileBytesTask, maxDepthTask);

        DocumentIngestionOptions normalized = DocumentIngestionOptions.Normalize(
            int.TryParse(chunkSizeValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int chunkSize)
                ? chunkSize
                : DocumentIngestionOptions.DefaultChunkSizeTokens,
            int.TryParse(overlapValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int overlap)
                ? overlap
                : DocumentIngestionOptions.DefaultOverlapTokens);

        return ToSettings(normalized, ArchiveExtractionLimits.Normalize(new ArchiveExtractionLimits(
            ParseInt(maxFileCountTask.Result, ArchiveExtractionLimits.DefaultMaxFileCount),
            ParseLong(maxTotalBytesTask.Result, ArchiveExtractionLimits.DefaultMaxTotalUncompressedBytes),
            ParseLong(maxFileBytesTask.Result, ArchiveExtractionLimits.DefaultMaxFileUncompressedBytes),
            ParseInt(maxDepthTask.Result, ArchiveExtractionLimits.DefaultMaxDirectoryDepth))));
    }

    public async Task<IngestionSettings> UpdateAsync(
        IngestionSettings settings,
        CancellationToken cancellationToken = default)
    {
        DocumentIngestionOptions normalized = DocumentIngestionOptions.Normalize(
            settings.ChunkSizeTokens,
            settings.OverlapTokens);

        await settingsRepository.UpsertAsync(
            ChunkSizeSettingKey,
            normalized.ChunkSizeTokens.ToString(CultureInfo.InvariantCulture),
            cancellationToken);
        await settingsRepository.UpsertAsync(
            OverlapSettingKey,
            normalized.OverlapTokens.ToString(CultureInfo.InvariantCulture),
            cancellationToken);
        ArchiveExtractionLimits archive = ArchiveExtractionLimits.Normalize(settings.Archive);
        await settingsRepository.UpsertAsync(ArchiveMaxFileCountSettingKey, archive.MaxFileCount.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await settingsRepository.UpsertAsync(ArchiveMaxTotalBytesSettingKey, archive.MaxTotalUncompressedBytes.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await settingsRepository.UpsertAsync(ArchiveMaxFileBytesSettingKey, archive.MaxFileUncompressedBytes.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await settingsRepository.UpsertAsync(ArchiveMaxDepthSettingKey, archive.MaxDirectoryDepth.ToString(CultureInfo.InvariantCulture), cancellationToken);

        return ToSettings(normalized, archive);
    }

    public static DocumentIngestionOptions ToOptions(IngestionSettings settings)
    {
        return DocumentIngestionOptions.Normalize(settings.ChunkSizeTokens, settings.OverlapTokens);
    }

    private static IngestionSettings ToSettings(DocumentIngestionOptions options, ArchiveExtractionLimits archive)
    {
        return new IngestionSettings(options.ChunkSizeTokens, options.OverlapTokens, archive);
    }

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;

    private static long ParseLong(string? value, long fallback) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : fallback;
}
