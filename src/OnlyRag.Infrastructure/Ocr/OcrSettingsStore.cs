using System.Globalization;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Ocr;

public sealed class OcrSettingsStore
{
    private const string ProfileKey = "ocr.profile";
    private const string PdfDpiKey = "ocr.pdfDpi";
    private const string DetectionSideLimitKey = "ocr.detectionSideLimit";
    private const string DetectionThresholdKey = "ocr.detectionThreshold";
    private const string RecognitionScoreThresholdKey = "ocr.recognitionScoreThreshold";
    private const string UseDocumentOrientationClassificationKey = "ocr.useDocumentOrientationClassification";
    private const string UseDocumentUnwarpingKey = "ocr.useDocumentUnwarping";
    private const string RecognitionBatchSizeKey = "ocr.recognitionBatchSize";
    private const string CpuThreadsKey = "ocr.cpuThreads";
    private const string DeviceKey = "ocr.device";
    private const string DeviceSourceKey = "ocr.deviceSource";
    private const string AutoDeviceSource = "auto";
    private const string ManualDeviceSource = "manual";

    private readonly ISettingsRepository settingsRepository;

    public OcrSettingsStore(ISettingsRepository settingsRepository)
    {
        this.settingsRepository = settingsRepository;
    }

    public async Task<OcrSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        OcrSettings defaults = OcrSettings.Default;
        return OcrSettings.Normalize(new OcrSettings(
            await ReadStringAsync(ProfileKey, defaults.Profile, cancellationToken),
            await ReadIntAsync(PdfDpiKey, defaults.PdfDpi, cancellationToken),
            await ReadIntAsync(DetectionSideLimitKey, defaults.DetectionSideLimit, cancellationToken),
            await ReadDoubleAsync(DetectionThresholdKey, defaults.DetectionThreshold, cancellationToken),
            await ReadDoubleAsync(RecognitionScoreThresholdKey, defaults.RecognitionScoreThreshold, cancellationToken),
            await ReadBoolAsync(UseDocumentOrientationClassificationKey, defaults.UseDocumentOrientationClassification, cancellationToken),
            await ReadBoolAsync(UseDocumentUnwarpingKey, defaults.UseDocumentUnwarping, cancellationToken),
            await ReadIntAsync(RecognitionBatchSizeKey, defaults.RecognitionBatchSize, cancellationToken),
            await ReadIntAsync(CpuThreadsKey, defaults.CpuThreads, cancellationToken),
            await ReadStringAsync(DeviceKey, defaults.Device, cancellationToken)));
    }

    public async Task<OcrSettings> UpdateAsync(
        OcrSettings settings,
        CancellationToken cancellationToken = default)
    {
        OcrSettings normalized = OcrSettings.Normalize(settings);
        await SaveAsync(normalized, ManualDeviceSource, cancellationToken);
        return normalized;
    }

    public async Task<OcrAutoGpuEnableResponse> AutoEnableGpuAsync(CancellationToken cancellationToken = default)
    {
        string? storedDevice = await settingsRepository.GetValueAsync(DeviceKey, cancellationToken);
        string? deviceSource = await settingsRepository.GetValueAsync(DeviceSourceKey, cancellationToken);
        OcrSettings current = await GetAsync(cancellationToken);

        if (current.Device == "gpu")
        {
            return new OcrAutoGpuEnableResponse(false, "OCR GPU già selezionata.", current);
        }

        if (!string.IsNullOrWhiteSpace(storedDevice)
            && !string.Equals(deviceSource, AutoDeviceSource, StringComparison.OrdinalIgnoreCase))
        {
            return new OcrAutoGpuEnableResponse(
                false,
                "OCR CPU mantenuto: esiste una preferenza dispositivo salvata manualmente.",
                current);
        }

        OcrSettings enabled = OcrSettings.Normalize(current with { Device = "gpu" });
        await SaveAsync(enabled, AutoDeviceSource, cancellationToken);
        return new OcrAutoGpuEnableResponse(true, "OCR GPU abilitata automaticamente.", enabled);
    }

    private async Task SaveAsync(
        OcrSettings normalized,
        string deviceSource,
        CancellationToken cancellationToken)
    {
        await settingsRepository.UpsertAsync(ProfileKey, normalized.Profile, cancellationToken);
        await settingsRepository.UpsertAsync(PdfDpiKey, normalized.PdfDpi.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await settingsRepository.UpsertAsync(DetectionSideLimitKey, normalized.DetectionSideLimit.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await settingsRepository.UpsertAsync(DetectionThresholdKey, normalized.DetectionThreshold.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await settingsRepository.UpsertAsync(RecognitionScoreThresholdKey, normalized.RecognitionScoreThreshold.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await settingsRepository.UpsertAsync(UseDocumentOrientationClassificationKey, normalized.UseDocumentOrientationClassification ? "true" : "false", cancellationToken);
        await settingsRepository.UpsertAsync(UseDocumentUnwarpingKey, normalized.UseDocumentUnwarping ? "true" : "false", cancellationToken);
        await settingsRepository.UpsertAsync(RecognitionBatchSizeKey, normalized.RecognitionBatchSize.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await settingsRepository.UpsertAsync(CpuThreadsKey, normalized.CpuThreads.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await settingsRepository.UpsertAsync(DeviceKey, normalized.Device, cancellationToken);
        await settingsRepository.UpsertAsync(DeviceSourceKey, deviceSource, cancellationToken);
    }

    public async Task<OcrProcessingSettings> GetProcessingAsync(CancellationToken cancellationToken = default)
    {
        string? language = await settingsRepository.GetValueAsync(OcrLanguageSettingKey, cancellationToken);
        string? retriesValue = await settingsRepository.GetValueAsync(OcrMaxRetriesSettingKey, cancellationToken);
        string? timeoutValue = await settingsRepository.GetValueAsync(OcrPageTimeoutSettingKey, cancellationToken);
        string? lowConfidenceValue = await settingsRepository.GetValueAsync(OcrLowConfidenceSettingKey, cancellationToken);

        return NormalizeProcessing(
            language,
            int.TryParse(retriesValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int retries) ? retries : null,
            int.TryParse(timeoutValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int timeout) ? timeout : null,
            double.TryParse(lowConfidenceValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double confidence) ? confidence : null);
    }

    public async Task<OcrProcessingSettings> UpdateProcessingAsync(
        OcrProcessingSettings settings,
        CancellationToken cancellationToken = default)
    {
        OcrProcessingSettings normalized = NormalizeProcessing(
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

    private static OcrProcessingSettings NormalizeProcessing(
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

    private const string OcrLanguageSettingKey = "ocr.language";
    private const string OcrMaxRetriesSettingKey = "ocr.maxRetries";
    private const string OcrPageTimeoutSettingKey = "ocr.pageTimeoutSeconds";
    private const string OcrLowConfidenceSettingKey = "ocr.lowConfidenceThreshold";

    private async Task<string> ReadStringAsync(string key, string defaultValue, CancellationToken cancellationToken)
    {
        string? value = await settingsRepository.GetValueAsync(key, cancellationToken);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    private async Task<int> ReadIntAsync(string key, int defaultValue, CancellationToken cancellationToken)
    {
        string? value = await settingsRepository.GetValueAsync(key, cancellationToken);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : defaultValue;
    }

    private async Task<double> ReadDoubleAsync(string key, double defaultValue, CancellationToken cancellationToken)
    {
        string? value = await settingsRepository.GetValueAsync(key, cancellationToken);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : defaultValue;
    }

    private async Task<bool> ReadBoolAsync(string key, bool defaultValue, CancellationToken cancellationToken)
    {
        string? value = await settingsRepository.GetValueAsync(key, cancellationToken);
        return bool.TryParse(value, out bool parsed) ? parsed : defaultValue;
    }
}

