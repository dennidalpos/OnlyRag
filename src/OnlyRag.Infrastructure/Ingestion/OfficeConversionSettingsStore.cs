using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Ingestion;

public sealed class OfficeConversionSettingsStore
{
    public const int DefaultConversionTimeoutSeconds = 120;
    public const int MinimumConversionTimeoutSeconds = 10;
    public const int MaximumConversionTimeoutSeconds = 900;

    private const string LibreOfficePathKey = "office.libreOfficePath";
    private const string ConversionTimeoutSecondsKey = "office.conversionTimeoutSeconds";

    private readonly ISettingsRepository settings;

    public OfficeConversionSettingsStore(ISettingsRepository settings)
    {
        this.settings = settings;
    }

    public async Task<OfficeConversionSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        string? path = NormalizeOptionalPath(await settings.GetValueAsync(LibreOfficePathKey, cancellationToken));
        string? timeoutValue = await settings.GetValueAsync(ConversionTimeoutSecondsKey, cancellationToken);

        return new OfficeConversionSettings(
            path,
            ParseTimeoutSeconds(timeoutValue));
    }

    public async Task<OfficeConversionSettings> UpdateAsync(
        OfficeConversionSettings request,
        CancellationToken cancellationToken = default)
    {
        string? path = NormalizeOptionalPath(request.LibreOfficePath);
        int timeout = ValidateTimeoutSeconds(request.ConversionTimeoutSeconds);

        await settings.UpsertAsync(LibreOfficePathKey, path ?? string.Empty, cancellationToken);
        await settings.UpsertAsync(ConversionTimeoutSecondsKey, timeout.ToString(), cancellationToken);

        return new OfficeConversionSettings(path, timeout);
    }

    public static int ValidateTimeoutSeconds(int timeoutSeconds)
    {
        if (timeoutSeconds < MinimumConversionTimeoutSeconds || timeoutSeconds > MaximumConversionTimeoutSeconds)
        {
            throw new OfficeConversionException(
                $"Il timeout conversione Office deve essere compreso tra {MinimumConversionTimeoutSeconds} e {MaximumConversionTimeoutSeconds} secondi.");
        }

        return timeoutSeconds;
    }

    private static int ParseTimeoutSeconds(string? timeoutValue)
    {
        return int.TryParse(timeoutValue, out int parsed)
            && parsed >= MinimumConversionTimeoutSeconds
            && parsed <= MaximumConversionTimeoutSeconds
                ? parsed
                : DefaultConversionTimeoutSeconds;
    }

    private static string? NormalizeOptionalPath(string? path)
    {
        string normalized = path?.Trim().Trim('"') ?? string.Empty;
        return normalized.Length == 0 ? null : normalized;
    }
}
