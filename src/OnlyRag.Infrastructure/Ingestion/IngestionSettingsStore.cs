using System.Globalization;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Ingestion;

public sealed class IngestionSettingsStore
{
    private const string ChunkSizeSettingKey = "ingestion.chunkSizeTokens";
    private const string OverlapSettingKey = "ingestion.overlapTokens";

    private readonly ISettingsRepository settingsRepository;

    public IngestionSettingsStore(ISettingsRepository settingsRepository)
    {
        this.settingsRepository = settingsRepository;
    }

    public async Task<IngestionSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        string? chunkSizeValue = await settingsRepository.GetValueAsync(ChunkSizeSettingKey, cancellationToken);
        string? overlapValue = await settingsRepository.GetValueAsync(OverlapSettingKey, cancellationToken);
        DocumentIngestionOptions normalized = DocumentIngestionOptions.Normalize(
            int.TryParse(chunkSizeValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int chunkSize)
                ? chunkSize
                : DocumentIngestionOptions.DefaultChunkSizeTokens,
            int.TryParse(overlapValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int overlap)
                ? overlap
                : DocumentIngestionOptions.DefaultOverlapTokens);

        return ToSettings(normalized);
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

        return ToSettings(normalized);
    }

    public static DocumentIngestionOptions ToOptions(IngestionSettings settings)
    {
        return DocumentIngestionOptions.Normalize(settings.ChunkSizeTokens, settings.OverlapTokens);
    }

    private static IngestionSettings ToSettings(DocumentIngestionOptions options)
    {
        return new IngestionSettings(options.ChunkSizeTokens, options.OverlapTokens);
    }
}
