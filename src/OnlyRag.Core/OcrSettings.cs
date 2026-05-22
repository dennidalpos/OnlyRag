using System.Globalization;

namespace OnlyRag.Core;

public sealed record OcrSettings(
    string Profile,
    int PdfDpi,
    string ModelPreset,
    string ModelVersion,
    int DetectionSideLimit,
    double DetectionThreshold,
    double DetectionBoxThreshold,
    double DetectionUnclipRatio,
    double RecognitionScoreThreshold,
    bool UseTextlineOrientation,
    bool UseDocumentOrientationClassification,
    bool UseDocumentUnwarping,
    int RecognitionBatchSize,
    int CpuThreads,
    string Device)
{
    public const string DefaultProfile = "balanced";
    public const int DefaultPdfDpi = 220;
    public const string DefaultModelPreset = "PP-OCRv5";
    public const string DefaultModelVersion = "PP-OCRv5";
    public const int DefaultDetectionSideLimit = 1152;
    public const double DefaultDetectionThreshold = 0.30d;
    public const double DefaultDetectionBoxThreshold = 0.60d;
    public const double DefaultDetectionUnclipRatio = 1.50d;
    public const double DefaultRecognitionScoreThreshold = 0.50d;
    public const bool DefaultUseTextlineOrientation = true;
    public const bool DefaultUseDocumentOrientationClassification = false;
    public const bool DefaultUseDocumentUnwarping = false;
    public const int DefaultRecognitionBatchSize = 6;
    public const int DefaultCpuThreads = 2;
    public const string DefaultDevice = "cpu";

    public static OcrSettings Default { get; } = ForProfile(DefaultProfile);

    public static OcrSettings ForProfile(string profile)
    {
        return ForProfile(profile, DefaultDevice);
    }

    public static OcrSettings ForProfile(string profile, string device)
    {
        return NormalizeProfile(profile) switch
        {
            "fast" => new OcrSettings(
                "fast",
                160,
                DefaultModelPreset,
                DefaultModelVersion,
                896,
                0.38d,
                0.68d,
                1.35d,
                0.58d,
                true,
                false,
                false,
                ResolveRecognitionBatchSize("fast", device, 4),
                1,
                NormalizeDevice(device)),
            "accurate" => new OcrSettings(
                "accurate",
                300,
                DefaultModelPreset,
                DefaultModelVersion,
                1536,
                0.23d,
                0.52d,
                1.75d,
                0.42d,
                true,
                true,
                true,
                ResolveRecognitionBatchSize("accurate", device, 8),
                4,
                NormalizeDevice(device)),
            _ => new OcrSettings(
                DefaultProfile,
                DefaultPdfDpi,
                DefaultModelPreset,
                DefaultModelVersion,
                DefaultDetectionSideLimit,
                DefaultDetectionThreshold,
                DefaultDetectionBoxThreshold,
                DefaultDetectionUnclipRatio,
                DefaultRecognitionScoreThreshold,
                DefaultUseTextlineOrientation,
                DefaultUseDocumentOrientationClassification,
                DefaultUseDocumentUnwarping,
                ResolveRecognitionBatchSize(DefaultProfile, device, DefaultRecognitionBatchSize),
                DefaultCpuThreads,
                NormalizeDevice(device))
        };
    }

    public static OcrSettings Normalize(OcrSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string profile = NormalizeProfile(settings.Profile);
        if (profile is not "custom")
        {
            return ForProfile(profile, settings.Device);
        }

        return new OcrSettings(
            profile,
            Math.Clamp(settings.PdfDpi, 96, 400),
            NormalizeToken(settings.ModelPreset, DefaultModelPreset, 64),
            NormalizeToken(settings.ModelVersion, DefaultModelVersion, 64),
            Math.Clamp(settings.DetectionSideLimit, 320, 4096),
            Math.Clamp(settings.DetectionThreshold, 0.01d, 0.99d),
            Math.Clamp(settings.DetectionBoxThreshold, 0.01d, 0.99d),
            Math.Clamp(settings.DetectionUnclipRatio, 1.0d, 3.0d),
            Math.Clamp(settings.RecognitionScoreThreshold, 0.01d, 0.99d),
            settings.UseTextlineOrientation,
            settings.UseDocumentOrientationClassification,
            settings.UseDocumentUnwarping,
            Math.Clamp(settings.RecognitionBatchSize, 1, 32),
            Math.Clamp(settings.CpuThreads, 1, 16),
            NormalizeDevice(settings.Device));
    }

    public string ToCacheSignature()
    {
        OcrSettings normalized = Normalize(this);
        return string.Join(
            '|',
            normalized.PdfDpi.ToString(CultureInfo.InvariantCulture),
            normalized.ModelPreset,
            normalized.ModelVersion,
            normalized.DetectionSideLimit.ToString(CultureInfo.InvariantCulture),
            normalized.DetectionThreshold.ToString("0.###", CultureInfo.InvariantCulture),
            normalized.DetectionBoxThreshold.ToString("0.###", CultureInfo.InvariantCulture),
            normalized.DetectionUnclipRatio.ToString("0.###", CultureInfo.InvariantCulture),
            normalized.RecognitionScoreThreshold.ToString("0.###", CultureInfo.InvariantCulture),
            normalized.UseTextlineOrientation ? "1" : "0",
            normalized.UseDocumentOrientationClassification ? "1" : "0",
            normalized.UseDocumentUnwarping ? "1" : "0",
            normalized.RecognitionBatchSize.ToString(CultureInfo.InvariantCulture),
            normalized.CpuThreads.ToString(CultureInfo.InvariantCulture),
            normalized.Device);
    }

    private static string NormalizeProfile(string value)
    {
        string normalized = NormalizeToken(value, DefaultProfile, 32).ToLowerInvariant();
        return normalized is "fast" or "balanced" or "accurate" or "custom"
            ? normalized
            : DefaultProfile;
    }

    private static string NormalizeDevice(string value)
    {
        string normalized = NormalizeToken(value, DefaultDevice, 32).ToLowerInvariant();
        return normalized is "cpu" or "gpu"
            ? normalized
            : DefaultDevice;
    }

    private static int ResolveRecognitionBatchSize(string profile, string device, int cpuBatchSize)
    {
        if (NormalizeDevice(device) != "gpu")
        {
            return cpuBatchSize;
        }

        return profile switch
        {
            "fast" => 8,
            "accurate" => 16,
            _ => 12
        };
    }

    private static string NormalizeToken(string value, string defaultValue, int maxLength)
    {
        string normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return defaultValue;
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
