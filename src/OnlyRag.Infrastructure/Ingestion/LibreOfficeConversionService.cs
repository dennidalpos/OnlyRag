using System.Diagnostics;
using System.Text;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Ingestion;

public sealed class LibreOfficeConversionService : IOfficeConversionService
{
    private static readonly string[] CandidateExecutablePaths =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LibreOffice", "program", "soffice.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LibreOffice", "program", "soffice.exe")
    ];

    private readonly LocalSqliteStoreDescriptor descriptor;
    private readonly OfficeConversionSettingsStore settingsStore;

    public LibreOfficeConversionService(
        LocalSqliteStoreDescriptor descriptor,
        OfficeConversionSettingsStore settingsStore)
    {
        this.descriptor = descriptor;
        this.settingsStore = settingsStore;
    }

    public async Task<OfficeConverterAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        OnlyRag.Core.OfficeConversionSettings settings = await settingsStore.GetAsync(cancellationToken);
        string? configuredPath = ResolveConfiguredExecutable(settings.LibreOfficePath);
        if (!string.IsNullOrWhiteSpace(settings.LibreOfficePath) && configuredPath is null)
        {
            return new OfficeConverterAvailability(
                false,
                null,
                "LibreOffice non trovato nel path configurato.",
                "Imposta il path di soffice.exe oppure lascia vuoto per usare il rilevamento automatico.");
        }

        string? executable = configuredPath
            ?? ResolveEnvironmentExecutable()
            ?? ResolveKnownInstallExecutable()
            ?? ResolvePathExecutable();

        return executable is null
            ? new OfficeConverterAvailability(
                false,
                null,
                "Convertitore Office legacy non disponibile.",
                "Installa LibreOffice per Windows oppure configura il path di soffice.exe in Impostazioni.")
            : new OfficeConverterAvailability(
                true,
                executable,
                "LibreOffice rilevato.",
                null);
    }

    public async Task<OfficeConversionResult> ConvertToPdfAsync(
        OfficeConversionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        OfficeConverterAvailability availability = await CheckAvailabilityAsync(cancellationToken);
        if (!availability.IsAvailable || string.IsNullOrWhiteSpace(availability.ExecutablePath))
        {
            throw new OfficeConversionUnavailableException(
                availability.Suggestion ?? availability.Message);
        }

        OnlyRag.Core.OfficeConversionSettings settings = await settingsStore.GetAsync(cancellationToken);
        int timeoutSeconds = OfficeConversionSettingsStore.ValidateTimeoutSeconds(settings.ConversionTimeoutSeconds);
        string tempRoot = Path.Combine(descriptor.Paths.DataRoot, "temp", "office-conversion");
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
                throw new OfficeConversionException("LibreOffice non ha prodotto un PDF per il documento Office.");
            }

            return new OfficeConversionResult(pdfPath, workDirectory);
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
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        StringBuilder output = new();
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
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
                throw new OfficeConversionException("Impossibile avviare LibreOffice.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new OfficeConversionException($"Timeout conversione Office dopo {timeout.TotalSeconds:0} secondi.");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        if (process.ExitCode != 0)
        {
            string details = output.ToString().Trim();
            throw new OfficeConversionException(
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

        return ResolveExecutableFromPath(configuredPath);
    }

    private static string? ResolveEnvironmentExecutable()
    {
        return ResolveExecutableFromPath(Environment.GetEnvironmentVariable("ONLYRAG_LIBREOFFICE_PATH"));
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
            string candidate = Path.Combine(directory.Trim(), "soffice.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? ResolveExecutableFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string normalized = path.Trim().Trim('"');
        if (File.Exists(normalized))
        {
            return normalized;
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
        return candidates.FirstOrDefault(File.Exists);
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
