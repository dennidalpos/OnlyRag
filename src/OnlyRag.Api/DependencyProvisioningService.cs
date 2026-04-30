using System.Diagnostics;
using System.Text.RegularExpressions;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ocr;

namespace OnlyRag.Api;

public sealed class DependencyProvisioningService
{
    public const string OllamaInstallCommand = "irm https://ollama.com/install.ps1 | iex";
    public const string OllamaNetworkAccessHint =
        "Per usare un endpoint Ollama da altri PC della LAN, configura OLLAMA_HOST nelle impostazioni/variabili ambiente di Ollama e riavvia Ollama.";

    private const string LibreOfficeDownloadUrl = "https://www.libreoffice.org/download/download-libreoffice/";
    private readonly object syncRoot = new();
    private OcrProvisionStatus lastOcrProvisionStatus = new(
        false,
        false,
        "OCR non configurato. Usa Configura OCR per preparare automaticamente le dipendenze locali.",
        null);
    private Task? ocrProvisionTask;

    public OllamaInstallStatus CreateOllamaStatus(bool apiReachable)
    {
        return new OllamaInstallStatus(
            ResolveExecutable("ollama") is not null,
            apiReachable,
            OllamaInstallCommand,
            OllamaNetworkAccessHint);
    }

    public DependencyActionResponse StartOllamaInstall()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException("Installazione automatica Ollama disponibile solo su Windows.");
        }

        string shell = ResolveExecutable("pwsh")
            ?? ResolveExecutable("powershell")
            ?? throw new InvalidOperationException("PowerShell non trovato. Installa PowerShell 7+ oppure esegui manualmente: " + OllamaInstallCommand);

        ProcessStartInfo startInfo = new()
        {
            FileName = shell,
            UseShellExecute = true
        };
        startInfo.ArgumentList.Add("-NoExit");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(OllamaInstallCommand);

        using Process process = new() { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("PowerShell non ha accettato la richiesta di installazione Ollama.");
        }

        return new DependencyActionResponse(true, $"Installazione Ollama avviata con: {OllamaInstallCommand}");
    }

    public DependencyActionResponse OpenLibreOfficeDownload()
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = LibreOfficeDownloadUrl,
            UseShellExecute = true
        };

        using Process process = new() { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Impossibile aprire la pagina di download LibreOffice.");
        }

        return new DependencyActionResponse(true, "Pagina download LibreOffice aperta.");
    }

    public async Task<OcrProvisionStatus> GetOcrStatusAsync(
        IOcrEngine ocrEngine,
        CancellationToken cancellationToken)
    {
        lock (syncRoot)
        {
            if (ocrProvisionTask is { IsCompleted: false })
            {
                return lastOcrProvisionStatus with { IsRunning = true };
            }
        }

        OcrEngineAvailability availability = await ocrEngine.CheckAvailabilityAsync(cancellationToken);
        if (availability.IsConfigured)
        {
            return new OcrProvisionStatus(
                true,
                false,
                $"OCR configurato: {availability.EngineName} {availability.EngineVersion}.",
                null);
        }

        string message = string.IsNullOrWhiteSpace(availability.Message)
            || availability.Message.Contains("Bootstrap", StringComparison.OrdinalIgnoreCase)
            ? "OCR non configurato. Usa Configura OCR per preparare automaticamente le dipendenze locali."
            : availability.Message;

        return new OcrProvisionStatus(false, false, message, null);
    }

    public DependencyActionResponse StartOcrProvision()
    {
        lock (syncRoot)
        {
            if (ocrProvisionTask is { IsCompleted: false })
            {
                return new DependencyActionResponse(false, "Configurazione OCR già in corso.");
            }

            lastOcrProvisionStatus = new OcrProvisionStatus(
                false,
                true,
                "Configurazione OCR avviata. La preparazione può richiedere diversi minuti.",
                null);
            ocrProvisionTask = Task.Run(ProvisionOcrAsync);
        }

        return new DependencyActionResponse(true, "Configurazione OCR avviata.");
    }

    private async Task ProvisionOcrAsync()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new InvalidOperationException("La configurazione automatica OCR è disponibile solo su Windows.");
            }

            string scriptsRoot = ResolveOcrScriptsRoot();
            string bridgePath = Path.Combine(scriptsRoot, "paddle_ocr_bridge.py");
            string requirementsPath = Path.Combine(scriptsRoot, "requirements.txt");
            if (!File.Exists(bridgePath) || !File.Exists(requirementsPath))
            {
                throw new InvalidOperationException("Runtime OCR incompleto: bridge o requirements non trovati.");
            }

            string python = ResolveExecutable("python")
                ?? throw new InvalidOperationException("Python 3.10+ non trovato. Installa Python per Windows, poi ripeti Configura OCR.");

            string versionText = (await RunProcessAsync(python, ["--version"], null, CancellationToken.None)).Trim();
            Version? version = ParseVersion(versionText);
            if (version is null || version.Major < 3 || (version.Major == 3 && version.Minor < 10))
            {
                throw new InvalidOperationException($"OCR richiede Python 3.10+; trovato '{versionText}'.");
            }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string installRoot = Path.Combine(localAppData, "OnlyRag", "ocr-python");
            string venvPath = Path.Combine(installRoot, ".venv");
            string venvPython = Path.Combine(venvPath, "Scripts", "python.exe");
            Directory.CreateDirectory(installRoot);

            if (!File.Exists(venvPython))
            {
                await RunProcessAsync(python, ["-m", "venv", venvPath], null, CancellationToken.None);
            }

            await RunProcessAsync(venvPython, ["-m", "pip", "install", "--upgrade", "pip", "--disable-pip-version-check"], null, CancellationToken.None);
            await RunProcessAsync(venvPython, ["-m", "pip", "install", "--upgrade", "-r", requirementsPath, "--disable-pip-version-check"], null, CancellationToken.None);
            await RunProcessAsync(venvPython, [bridgePath, "--mode", "check"], null, CancellationToken.None);

            SetLastOcrStatus(new OcrProvisionStatus(
                true,
                false,
                "OCR configurato correttamente. Puoi usare l'OCR da importazione documenti e azioni documento.",
                null));
        }
        catch (Exception ex)
        {
            SetLastOcrStatus(new OcrProvisionStatus(
                false,
                false,
                "Configurazione OCR non completata.",
                ex.Message));
        }
    }

    private void SetLastOcrStatus(OcrProvisionStatus status)
    {
        lock (syncRoot)
        {
            lastOcrProvisionStatus = status;
        }
    }

    private static string ResolveOcrScriptsRoot()
    {
        string outputRoot = Path.Combine(AppContext.BaseDirectory, "scripts", "ocr");
        if (Directory.Exists(outputRoot))
        {
            return outputRoot;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "ocr"));
    }

    private static async Task<string> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Impossibile avviare {fileName}.");
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException($"Impossibile avviare {fileName}: {ex.Message}", ex);
        }

        string stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        string stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
                ? $"{fileName} terminato con exit code {process.ExitCode}."
                : detail.Trim());
        }

        return string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
    }

    private static Version? ParseVersion(string text)
    {
        Match match = Regex.Match(text, @"(\d+)\.(\d+)\.(\d+)");
        return match.Success
            ? new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value))
            : null;
    }

    private static string? ResolveExecutable(string executableName)
    {
        string normalizedName = executableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? executableName
            : executableName + ".exe";

        IEnumerable<string> candidateDirectories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (string.Equals(normalizedName, "ollama.exe", StringComparison.OrdinalIgnoreCase))
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            candidateDirectories = candidateDirectories.Concat([
                Path.Combine(localAppData, "Programs", "Ollama")
            ]);
        }

        foreach (string directory in candidateDirectories)
        {
            string candidate = Path.Combine(directory, normalizedName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
