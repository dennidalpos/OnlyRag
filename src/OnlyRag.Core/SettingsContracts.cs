using System.Globalization;

namespace OnlyRag.Core;

public sealed record OllamaSettings(
    string OllamaBaseUrl,
    string? DefaultChatModel,
    string? DefaultEmbeddingModel,
    string? DefaultTranslationModel,
    int RequestTimeoutSeconds,
    int EmbeddingBatchSize,
    string? DefaultCodingModel = null,
    int? EmbeddingNumCtx = null,
    int? ChatNumCtx = null,
    int? TranslationNumCtx = null,
    int? CodingNumCtx = null,
    bool TrustNonLocalEndpoint = false);

public sealed record IngestionSettings(
    int ChunkSizeTokens,
    int OverlapTokens);

public enum AppLogLevel
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    None = 5
}

public sealed record LoggingSettings(
    AppLogLevel MinLevel = AppLogLevel.Trace);

public sealed record OcrProcessingSettings(
    string Language,
    int MaxRetries,
    int PageTimeoutSeconds,
    double LowConfidenceThreshold);

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
    public const string DefaultDevice = "auto";

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
        return normalized is "auto" or "cpu" or "gpu"
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

public sealed record OcrLanguage(string Code, string Label, string ScriptGroup, bool IsDefault = false);

public static class OcrLanguages
{
    public const string DefaultCode = "it";

    public static IReadOnlyList<OcrLanguage> All { get; } =
    [
        new("it", "Italiano", "Principali", IsDefault: true),
        new("en", "Inglese", "Principali"),
        new("fr", "Francese", "Principali"),
        new("de", "Tedesco", "Principali"),
        new("es", "Spagnolo", "Principali"),
        new("pt", "Portoghese", "Principali"),
        new("nl", "Olandese", "Principali"),
        new("pl", "Polacco", "Principali"),
        new("ro", "Rumeno", "Principali"),
        new("sv", "Svedese", "Principali"),
        new("da", "Danese", "Principali"),
        new("no", "Norvegese", "Principali"),
        new("fi", "Finlandese", "Principali"),
        new("cs", "Ceco", "Principali"),
        new("sk", "Slovacco", "Principali"),
        new("sl", "Sloveno", "Principali"),
        new("hr", "Croato", "Principali"),
        new("bs", "Bosniaco", "Principali"),
        new("hu", "Ungherese", "Principali"),
        new("tr", "Turco", "Principali"),
        new("el", "Greco", "Principali"),
        new("ru", "Russo", "Principali"),
        new("uk", "Ucraino", "Principali"),
        new("be", "Bielorusso", "Principali"),
        new("bg", "Bulgaro", "Principali"),
        new("rs_latin", "Serbo latino", "Principali"),
        new("rs_cyrillic", "Serbo cirillico", "Principali"),
        new("ar", "Arabo", "Principali"),
        new("fa", "Persiano", "Principali"),
        new("ur", "Urdu", "Principali"),
        new("hi", "Hindi", "Principali"),
        new("ta", "Tamil", "Principali"),
        new("te", "Telugu", "Principali"),
        new("th", "Thai", "Principali"),
        new("vi", "Vietnamita", "Principali"),
        new("id", "Indonesiano", "Principali"),
        new("ms", "Malese", "Principali"),
        new("ch", "Cinese semplificato + inglese", "Principali"),
        new("chinese_cht", "Cinese tradizionale", "Principali"),
        new("japan", "Giapponese", "Principali"),
        new("korean", "Coreano", "Principali"),
        new("af", "Afrikaans", "Avanzate"),
        new("sq", "Albanese", "Avanzate"),
        new("az", "Azero", "Avanzate"),
        new("eu", "Basco", "Avanzate"),
        new("ca", "Catalano", "Avanzate"),
        new("cy", "Gallese", "Avanzate"),
        new("et", "Estone", "Avanzate"),
        new("ga", "Irlandese", "Avanzate"),
        new("gl", "Galiziano", "Avanzate"),
        new("is", "Islandese", "Avanzate"),
        new("la", "Latino", "Avanzate"),
        new("lt", "Lituano", "Avanzate"),
        new("lv", "Lettone", "Avanzate"),
        new("lb", "Lussemburghese", "Avanzate"),
        new("mt", "Maltese", "Avanzate"),
        new("mi", "Maori", "Avanzate"),
        new("oc", "Occitano", "Avanzate"),
        new("qu", "Quechua", "Avanzate"),
        new("rm", "Romancio", "Avanzate"),
        new("sw", "Swahili", "Avanzate"),
        new("tl", "Tagalog", "Avanzate"),
        new("uz", "Uzbeko", "Avanzate"),
        new("ku", "Curdo", "Avanzate"),
        new("pi", "Pali", "Avanzate"),
        new("kk", "Kazako", "Avanzate"),
        new("ky", "Kirghiso", "Avanzate"),
        new("tg", "Tagiko", "Avanzate"),
        new("tt", "Tataro", "Avanzate"),
        new("mn", "Mongolo", "Avanzate"),
        new("mk", "Macedone", "Avanzate"),
        new("cv", "Ciuvascio", "Avanzate"),
        new("ba", "Baschiro", "Avanzate"),
        new("mhr", "Mari", "Avanzate"),
        new("mo", "Moldavo", "Avanzate"),
        new("udm", "Udmurto", "Avanzate"),
        new("kv", "Komi", "Avanzate"),
        new("os", "Osseto", "Avanzate"),
        new("bua", "Buriato", "Avanzate"),
        new("xal", "Calmucco", "Avanzate"),
        new("tyv", "Tuvano", "Avanzate"),
        new("sah", "Sakha", "Avanzate"),
        new("kaa", "Karakalpako", "Avanzate"),
        new("ug", "Uiguro", "Avanzate"),
        new("ps", "Pashto", "Avanzate"),
        new("sd", "Sindhi", "Avanzate"),
        new("bal", "Baloci", "Avanzate"),
        new("mr", "Marathi", "Avanzate"),
        new("ne", "Nepalese", "Avanzate"),
        new("bh", "Bihari", "Avanzate"),
        new("mai", "Maithili", "Avanzate"),
        new("ang", "Inglese antico", "Avanzate"),
        new("bho", "Bhojpuri", "Avanzate"),
        new("mah", "Magahi", "Avanzate"),
        new("sck", "Sadri", "Avanzate"),
        new("new", "Newar", "Avanzate"),
        new("gom", "Konkani", "Avanzate"),
        new("sa", "Sanscrito", "Avanzate"),
        new("bgc", "Haryanvi", "Avanzate"),
        new("ab", "Abkhazo", "Avanzate"),
        new("ady", "Adighè", "Avanzate"),
        new("kbd", "Cabardino", "Avanzate"),
        new("av", "Avaro", "Avanzate"),
        new("dar", "Dargwa", "Avanzate"),
        new("inh", "Inguscio", "Avanzate"),
        new("ce", "Ceceno", "Avanzate"),
        new("lki", "Lak", "Avanzate"),
        new("lez", "Lezgino", "Avanzate"),
        new("tab", "Tabasarano", "Avanzate")
    ];

    public static string NormalizeCode(string? code)
    {
        string normalized = string.IsNullOrWhiteSpace(code)
            ? DefaultCode
            : code.Trim();

        return All.FirstOrDefault(language => string.Equals(language.Code, normalized, StringComparison.OrdinalIgnoreCase))?.Code
            ?? DefaultCode;
    }
}

public sealed record PdfExportSettings(
    string? LibreOfficePath,
    int ConversionTimeoutSeconds);

public static class PerformanceProfileNames
{
    public const string Auto = "auto";
    public const string Eco = "eco";
    public const string Balanced = "balanced";
    public const string Power = "power";
    public const string Custom = "custom";
}

public sealed record PerformanceSettings(
    int MaxParallelJobs,
    int MaxOcrParallelPages,
    int EmbeddingBatchSize,
    int TranslationBatchSize,
    int MaxContextChunks,
    int RequestTimeoutSeconds,
    bool EnableLowResourceMode,
    string Profile = PerformanceProfileNames.Auto,
    string EffectiveProfile = PerformanceProfileNames.Eco);

public sealed record UnifiedSettingsResponse(
    OllamaSettings Ollama,
    QdrantSettingsResponse Qdrant,
    PerformanceSettings Performance,
    IngestionSettings Ingestion,
    OcrSettings Ocr,
    OcrProcessingSettings OcrProcessing,
    PdfExportSettings PdfExport,
    LoggingSettings Logging);

public sealed record UnifiedSettingsUpdateRequest(
    OllamaSettings? Ollama = null,
    QdrantSettings? Qdrant = null,
    PerformanceSettings? Performance = null,
    IngestionSettings? Ingestion = null,
    OcrSettings? Ocr = null,
    OcrProcessingSettings? OcrProcessing = null,
    PdfExportSettings? PdfExport = null,
    LoggingSettings? Logging = null);
