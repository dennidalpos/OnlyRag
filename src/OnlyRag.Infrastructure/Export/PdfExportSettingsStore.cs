using System.Globalization;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Export;

public sealed class PdfExportSettingsStore
{
    private const string LibreOfficePathSettingKey = "pdfExport.libreOfficePath";
    private const string ConversionTimeoutSettingKey = "pdfExport.conversionTimeoutSeconds";
    public const int DefaultConversionTimeoutSeconds = 120;
    public const int MinConversionTimeoutSeconds = 10;
    public const int MaxConversionTimeoutSeconds = 3600;

    private readonly ISettingsRepository settingsRepository;

    public PdfExportSettingsStore(ISettingsRepository settingsRepository)
    {
        this.settingsRepository = settingsRepository;
    }

    public async Task<PdfExportSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        string? pathValue = await settingsRepository.GetValueAsync(LibreOfficePathSettingKey, cancellationToken);
        string? timeoutValue = await settingsRepository.GetValueAsync(ConversionTimeoutSettingKey, cancellationToken);

        string? libreOfficePath = string.IsNullOrWhiteSpace(pathValue) ? null : pathValue.Trim();
        int conversionTimeoutSeconds = int.TryParse(timeoutValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int timeout)
            ? NormalizeTimeout(timeout)
            : DefaultConversionTimeoutSeconds;

        return new PdfExportSettings(libreOfficePath, conversionTimeoutSeconds);
    }

    public async Task<PdfExportSettings> UpdateAsync(
        PdfExportSettings settings,
        CancellationToken cancellationToken = default)
    {
        PdfExportSettings normalized = Normalize(settings);

        await settingsRepository.UpsertAsync(
            LibreOfficePathSettingKey,
            normalized.LibreOfficePath ?? string.Empty,
            cancellationToken);
        await settingsRepository.UpsertAsync(
            ConversionTimeoutSettingKey,
            normalized.ConversionTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
            cancellationToken);

        return normalized;
    }

    public async Task<PdfExportConverterStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        PdfExportSettings settings = await GetAsync(cancellationToken);
        string? resolvedPath = ResolveLibreOfficeExecutable(settings.LibreOfficePath);

        bool isAvailable = resolvedPath is not null;
        string state = isAvailable ? "Available" : "Missing";
        string message = isAvailable
            ? "LibreOffice e pronto per la conversione in PDF."
            : "LibreOffice non installato.";
        string? suggestion = isAvailable
            ? null
            : "Installa LibreOffice per abilitare l'esportazione PDF.";

        return new PdfExportConverterStatusResponse(
            state,
            isAvailable,
            resolvedPath ?? settings.LibreOfficePath,
            message,
            suggestion,
            settings.ConversionTimeoutSeconds);
    }

    public static PdfExportSettings Normalize(PdfExportSettings settings)
    {
        string? normalizedPath = string.IsNullOrWhiteSpace(settings.LibreOfficePath)
            ? null
            : settings.LibreOfficePath.Trim();

        return new PdfExportSettings(normalizedPath, NormalizeTimeout(settings.ConversionTimeoutSeconds));
    }

    private static int NormalizeTimeout(int seconds)
    {
        if (seconds <= 0)
        {
            return DefaultConversionTimeoutSeconds;
        }

        return Math.Clamp(seconds, MinConversionTimeoutSeconds, MaxConversionTimeoutSeconds);
    }

    public static string? ResolveLibreOfficeExecutable(
        string? customPath = null,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? directoryExists = null)
    {
        fileExists ??= File.Exists;
        directoryExists ??= Directory.Exists;

        if (!string.IsNullOrWhiteSpace(customPath))
        {
            string trimmed = customPath.Trim();
            if (fileExists(trimmed))
            {
                return Path.GetFullPath(trimmed);
            }

            return null;
        }

        string executableName = OperatingSystem.IsWindows() ? "soffice.exe" : "soffice";

        string[] searchDirectories = [
            .. (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            @"C:\Program Files\LibreOffice\program",
            @"C:\Program Files (x86)\LibreOffice\program",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "LibreOffice", "program")
        ];

        foreach (string directory in searchDirectories)
        {
            try
            {
                if (directoryExists(directory))
                {
                    string candidate = Path.Combine(directory, executableName);
                    if (fileExists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
            }
            catch
            {
                // Ignore path access exception
            }
        }

        return null;
    }
}
