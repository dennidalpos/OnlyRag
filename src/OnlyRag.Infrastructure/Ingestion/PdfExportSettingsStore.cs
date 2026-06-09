using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Ingestion;

public sealed class PdfExportSettingsStore
{
    public const int DefaultConversionTimeoutSeconds = 120;
    public const int MinimumConversionTimeoutSeconds = 10;
    public const int MaximumConversionTimeoutSeconds = 900;

    private const string LibreOfficePathKey = "pdfExport.libreOfficePath";
    private const string ConversionTimeoutSecondsKey = "pdfExport.conversionTimeoutSeconds";

    private readonly ISettingsRepository settings;

    public PdfExportSettingsStore(ISettingsRepository settings)
    {
        this.settings = settings;
    }

    public async Task<PdfExportSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        string? path = NormalizeOptionalPath(await settings.GetValueAsync(LibreOfficePathKey, cancellationToken));
        string? timeoutValue = await settings.GetValueAsync(ConversionTimeoutSecondsKey, cancellationToken);

        return new PdfExportSettings(
            path,
            ParseTimeoutSeconds(timeoutValue));
    }

    public async Task<PdfExportSettings> UpdateAsync(
        PdfExportSettings request,
        CancellationToken cancellationToken = default)
    {
        string? path = NormalizeLibreOfficePath(request.LibreOfficePath);
        int timeout = ValidateTimeoutSeconds(request.ConversionTimeoutSeconds);

        await settings.UpsertAsync(LibreOfficePathKey, path ?? string.Empty, cancellationToken);
        await settings.UpsertAsync(ConversionTimeoutSecondsKey, timeout.ToString(), cancellationToken);

        return new PdfExportSettings(path, timeout);
    }

    public static string? NormalizeLibreOfficePath(string? path)
    {
        string? normalized = NormalizeOptionalPath(path);
        if (normalized is null)
        {
            return null;
        }

        string? executable = TryResolveLibreOfficeExecutable(normalized);
        if (executable is null)
        {
            throw new PdfExportConversionException(
                "Il percorso LibreOffice per export PDF deve puntare a soffice.exe o a una cartella di installazione LibreOffice che contiene soffice.exe.");
        }

        return executable;
    }

    public static string? TryResolveLibreOfficeExecutable(string? path)
    {
        string? normalized = NormalizeOptionalPath(path);
        if (normalized is null)
        {
            return null;
        }

        if (File.Exists(normalized))
        {
            return IsLibreOfficeExecutable(normalized)
                ? Path.GetFullPath(normalized)
                : null;
        }

        if (!Directory.Exists(normalized))
        {
            return null;
        }

        string[] candidates =
        [
            Path.Combine(normalized, "soffice.exe"),
            Path.Combine(normalized, "program", "soffice.exe")
        ];
        return candidates
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .FirstOrDefault(IsLibreOfficeExecutable);
    }

    public static int ValidateTimeoutSeconds(int timeoutSeconds)
    {
        if (timeoutSeconds < MinimumConversionTimeoutSeconds || timeoutSeconds > MaximumConversionTimeoutSeconds)
        {
            throw new PdfExportConversionException(
                $"Il timeout export PDF deve essere compreso tra {MinimumConversionTimeoutSeconds} e {MaximumConversionTimeoutSeconds} secondi.");
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

    private static bool IsLibreOfficeExecutable(string path)
    {
        return string.Equals(Path.GetFileName(path), "soffice.exe", StringComparison.OrdinalIgnoreCase);
    }
}
