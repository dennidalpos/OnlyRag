using System.Globalization;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Ocr;

public sealed class OcrProcessingSettingsStore
{
    private const string OcrLanguageSettingKey = "ocr.language";
    private const string OcrMaxRetriesSettingKey = "ocr.maxRetries";
    private const string OcrPageTimeoutSettingKey = "ocr.pageTimeoutSeconds";
    private const string OcrLowConfidenceSettingKey = "ocr.lowConfidenceThreshold";

    private readonly ISettingsRepository settingsRepository;

    public OcrProcessingSettingsStore(ISettingsRepository settingsRepository)
    {
        this.settingsRepository = settingsRepository;
    }

    public async Task<OcrProcessingSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        string? language = await settingsRepository.GetValueAsync(OcrLanguageSettingKey, cancellationToken);
        string? retriesValue = await settingsRepository.GetValueAsync(OcrMaxRetriesSettingKey, cancellationToken);
        string? timeoutValue = await settingsRepository.GetValueAsync(OcrPageTimeoutSettingKey, cancellationToken);
        string? lowConfidenceValue = await settingsRepository.GetValueAsync(OcrLowConfidenceSettingKey, cancellationToken);

        return Normalize(
            language,
            int.TryParse(retriesValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int retries) ? retries : null,
            int.TryParse(timeoutValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int timeout) ? timeout : null,
            double.TryParse(lowConfidenceValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double confidence) ? confidence : null);
    }

    public async Task<OcrProcessingSettings> UpdateAsync(
        OcrProcessingSettings settings,
        CancellationToken cancellationToken = default)
    {
        OcrProcessingSettings normalized = Normalize(
            settings.Language,
            settings.MaxRetries,
            settings.PageTimeoutSeconds,
            settings.LowConfidenceThreshold);

        await settingsRepository.UpsertAsync(OcrLanguageSettingKey, normalized.Language, cancellationToken);
        await settingsRepository.UpsertAsync(
            OcrMaxRetriesSettingKey,
            normalized.MaxRetries.ToString(CultureInfo.InvariantCulture),
            cancellationToken);
        await settingsRepository.UpsertAsync(
            OcrPageTimeoutSettingKey,
            normalized.PageTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
            cancellationToken);
        await settingsRepository.UpsertAsync(
            OcrLowConfidenceSettingKey,
            normalized.LowConfidenceThreshold.ToString(CultureInfo.InvariantCulture),
            cancellationToken);

        return normalized;
    }

    private static OcrProcessingSettings Normalize(
        string? language,
        int? maxRetries,
        int? pageTimeoutSeconds,
        double? lowConfidenceThreshold)
    {
        OcrPipelineOptions normalized = OcrPipelineOptions.Normalize(
            language,
            maxRetries,
            pageTimeoutSeconds,
            lowConfidenceThreshold);

        return new OcrProcessingSettings(
            normalized.Language,
            normalized.MaxRetries,
            (int)normalized.PageTimeout.TotalSeconds,
            normalized.LowConfidenceThreshold);
    }
}
