using System.Diagnostics;
using System.Text;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Ingestion;

public sealed class LibreOfficePdfExportConverter : IPdfExportConverter
{
    private static readonly string[] CandidateExecutablePaths =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LibreOffice", "program", "soffice.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LibreOffice", "program", "soffice.exe")
    ];

    private readonly LocalSqliteStoreDescriptor descriptor;
    private readonly PdfExportSettingsStore settingsStore;

    public LibreOfficePdfExportConverter(
        LocalSqliteStoreDescriptor descriptor,
        PdfExportSettingsStore settingsStore)
    {
        this.descriptor = descriptor;
        this.settingsStore = settingsStore;
    }

    public async Task<PdfExportConverterAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        OnlyRag.Core.PdfExportSettings settings = await settingsStore.GetAsync(cancellationToken);
        string? configuredPath = ResolveConfiguredExecutable(settings.LibreOfficePath);
        if (!string.IsNullOrWhiteSpace(settings.LibreOfficePath) && configuredPath is null)
        {
            return new PdfExportConverterAvailability(
                false,
                null,
                "LibreOffice non trovato nel path configurato per export PDF.",
                "Imposta il path di soffice.exe oppure lascia vuoto per usare il rilevamento automatico.");
        }

        string? executable = configuredPath
            ?? ResolveEnvironmentExecutable()
            ?? ResolveKnownInstallExecutable()
            ?? ResolvePathExecutable();

        return executable is null
            ? new PdfExportConverterAvailability(
                false,
                null,
                "Convertitore PDF non disponibile.",
                "Installa LibreOffice per Windows oppure configura il path di soffice.exe in Impostazioni per abilitare l'export PDF.")
            : new PdfExportConverterAvailability(
                true,
                executable,
                "LibreOffice rilevato.",
                null);
    }

    public async Task<PdfExportConversionResult> ConvertToPdfAsync(
        PdfExportConversionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PdfExportConverterAvailability availability = await CheckAvailabilityAsync(cancellationToken);
        if (!availability.IsAvailable || string.IsNullOrWhiteSpace(availability.ExecutablePath))
        {
            throw new PdfExportConversionUnavailableException(
                availability.Suggestion ?? availability.Message);
        }

        OnlyRag.Core.PdfExportSettings settings = await settingsStore.GetAsync(cancellationToken);
        int timeoutSeconds = PdfExportSettingsStore.ValidateTimeoutSeconds(settings.ConversionTimeoutSeconds);
        string tempRoot = Path.Combine(descriptor.Paths.TempDirectory, "pdf-export");
        string workDirectory = Path.Combine(tempRoot, $"{request.DocumentId}-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(workDirectory);
            string inputName = SafeDocumentPath.NormalizeFileName(request.OriginalFileName);
            string inputPath = SafeDocumentPath.ResolveWithinRoot(workDirectory, inputName);
            File.Copy(request.SourcePath, inputPath, overwrite: false);

            await RunLibreOfficeAsync(
                availability.ExecutablePath,
                inputPath,
                workDirectory,
                TimeSpan.FromSeconds(timeoutSeconds),
                cancellationToken);

            string? pdfPath = Directory
                .EnumerateFiles(workDirectory, "*.pdf", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (pdfPath is null || !File.Exists(pdfPath))
            {
                throw new PdfExportConversionException("LibreOffice non ha prodotto un PDF per l'export.");
            }

            return new PdfExportConversionResult(pdfPath, workDirectory);
        }
        catch
        {
            TryDeleteDirectory(workDirectory);
            throw;
        }
    }

    private static async Task RunLibreOfficeAsync(
        string executablePath,
        string inputPath,
        string outputDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        string trustedExecutablePath = PdfExportSettingsStore.TryResolveLibreOfficeExecutable(executablePath)
            ?? throw new PdfExportConversionException("Percorso LibreOffice non valido: usa soffice.exe.");
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        StringBuilder output = new();
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = trustedExecutablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                WorkingDirectory = outputDirectory
            }
        };
        process.StartInfo.ArgumentList.Add("--headless");
        process.StartInfo.ArgumentList.Add("--nologo");
        process.StartInfo.ArgumentList.Add("--nofirststartwizard");
        process.StartInfo.ArgumentList.Add("--nolockcheck");
        process.StartInfo.ArgumentList.Add("--nodefault");
        process.StartInfo.ArgumentList.Add("--convert-to");
        process.StartInfo.ArgumentList.Add("pdf");
        process.StartInfo.ArgumentList.Add("--outdir");
        process.StartInfo.ArgumentList.Add(outputDirectory);
        process.StartInfo.ArgumentList.Add(inputPath);

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                output.AppendLine(eventArgs.Data);
            }
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                output.AppendLine(eventArgs.Data);
            }
        };

        try
        {
            if (!process.Start())
            {
                throw new PdfExportConversionException("Impossibile avviare LibreOffice.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new PdfExportConversionException($"Timeout export PDF dopo {timeout.TotalSeconds:0} secondi.");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        if (process.ExitCode != 0)
        {
            string details = output.ToString().Trim();
            throw new PdfExportConversionException(
                details.Length == 0
                    ? $"LibreOffice ha terminato con exit code {process.ExitCode}."
                    : $"LibreOffice ha terminato con exit code {process.ExitCode}: {details}");
        }
    }

    private static string? ResolveConfiguredExecutable(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        return PdfExportSettingsStore.TryResolveLibreOfficeExecutable(configuredPath);
    }

    private static string? ResolveEnvironmentExecutable()
    {
        return PdfExportSettingsStore.TryResolveLibreOfficeExecutable(Environment.GetEnvironmentVariable("ONLYRAG_LIBREOFFICE_PATH"));
    }

    private static string? ResolveKnownInstallExecutable()
    {
        return CandidateExecutablePaths.FirstOrDefault(File.Exists);
    }

    private static string? ResolvePathExecutable()
    {
        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return null;
        }

        foreach (string directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string? candidate = PdfExportSettingsStore.TryResolveLibreOfficeExecutable(
                Path.Combine(directory.Trim(), "soffice.exe"));
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
        }
    }
}
