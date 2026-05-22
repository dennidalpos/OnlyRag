using System.Diagnostics;
using System.Text.RegularExpressions;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ocr;

namespace OnlyRag.Api;

public sealed class DependencyProvisioningService
{
    public const string OllamaDownloadUrl = "https://ollama.com/download";
    public const string OllamaInstallCommand = OllamaDownloadUrl;
    public const string OllamaNetworkAccessHint =
        "Installa Ollama manualmente dalla pagina ufficiale. Se sei offline o una policy aziendale blocca download o browser esterni, scarica il programma da una rete approvata o chiedi al reparto IT. Per usare un endpoint Ollama da altri PC della LAN, configura OLLAMA_HOST nelle impostazioni/variabili ambiente di Ollama e riavvia Ollama.";

    private const string LibreOfficeDownloadUrl = "https://www.libreoffice.org/download/download-libreoffice/";
    private static readonly int[] SupportedOcrPythonMinors = [13, 12, 11, 10];
    private readonly ILocalProcessLauncher processLauncher;
    private readonly object syncRoot = new();
    private OcrProvisionStatus lastOcrProvisionStatus = new(
        false,
        false,
        "OCR non configurato. Usa Configura OCR per preparare automaticamente le dipendenze locali.",
        null);
    private Task? ocrProvisionTask;

    public DependencyProvisioningService(ILocalProcessLauncher processLauncher)
    {
        this.processLauncher = processLauncher;
    }

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
        ProcessStartInfo startInfo = new()
        {
            FileName = OllamaDownloadUrl,
            UseShellExecute = true
        };

        if (!processLauncher.TryStart(startInfo, out _))
        {
            throw new InvalidOperationException(
                "Download Ollama non aperto. Apri manualmente https://ollama.com/download. " +
                "Se sei offline o una policy aziendale blocca browser o download esterni, usa una rete approvata o chiedi al reparto IT.");
        }

        return new DependencyActionResponse(
            true,
            "Pagina download Ollama aperta. Installa manualmente Ollama, avvialo, poi torna in OnlyRag e usa Verifica ora.");
    }

    public DependencyActionResponse OpenLibreOfficeDownload()
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = LibreOfficeDownloadUrl,
            UseShellExecute = true
        };

        if (!processLauncher.TryStart(startInfo, out string? errorMessage))
        {
            throw new InvalidOperationException(errorMessage ?? "Impossibile aprire la pagina di download LibreOffice.");
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

            OcrPythonCommand python = await ResolveOcrPythonCommandAsync(CancellationToken.None);

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string installRoot = Path.Combine(localAppData, "OnlyRag", "ocr-python");
            string venvPath = Path.Combine(installRoot, ".venv");
            string venvPython = Path.Combine(venvPath, "Scripts", "python.exe");
            Directory.CreateDirectory(installRoot);

            bool createVenv = !File.Exists(venvPython);
            if (!createVenv)
            {
                LocalProcessResult venvVersionResult = await processLauncher.RunAsync(
                    venvPython,
                    ["--version"],
                    null,
                    CancellationToken.None);
                string venvVersionText = GetProcessVersionText(venvVersionResult);
                Version? venvVersion = ParseVersion(venvVersionText);
                if (venvVersionResult.ExitCode != 0 || venvVersion is null || !IsSupportedOcrPythonVersion(venvVersion))
                {
                    Directory.Delete(venvPath, recursive: true);
                    createVenv = true;
                }
            }

            if (createVenv)
            {
                await RunProcessAsync(
                    python.FileName,
                    python.WithArguments(["-m", "venv", venvPath]),
                    null,
                    CancellationToken.None);
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

    private async Task<OcrPythonCommand> ResolveOcrPythonCommandAsync(CancellationToken cancellationToken)
    {
        List<OcrPythonCommand> candidates = [];
        string? python = ResolveExecutable("python");
        if (python is not null)
        {
            candidates.Add(new OcrPythonCommand(python, []));
        }

        string? py = ResolveExecutable("py");
        if (py is not null)
        {
            candidates.AddRange(SupportedOcrPythonMinors.Select(minor => new OcrPythonCommand(py, [$"-3.{minor}"])));
        }

        List<string> unsupported = [];
        foreach (OcrPythonCommand candidate in candidates)
        {
            LocalProcessResult result = await processLauncher.RunAsync(
                candidate.FileName,
                candidate.WithArguments(["--version"]),
                null,
                cancellationToken);
            if (result.ExitCode != 0)
            {
                continue;
            }

            string versionText = GetProcessVersionText(result);
            Version? version = ParseVersion(versionText);
            if (version is null)
            {
                continue;
            }

            if (IsSupportedOcrPythonVersion(version))
            {
                return candidate;
            }

            unsupported.Add(versionText);
        }

        string detail = unsupported.Count == 0
            ? "Python compatibile non trovato."
            : $"Interpreti non compatibili trovati: {string.Join(", ", unsupported)}.";
        throw new InvalidOperationException(
            "OCR richiede Python 3.10, 3.11, 3.12 o 3.13. " +
            "PaddlePaddle 3.3.1 non pubblica wheel Windows per Python 3.14. " +
            $"{detail} Installa una versione compatibile di Python per Windows, poi ripeti Configura OCR.");
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

    private async Task<string> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        LocalProcessResult result = await processLauncher.RunAsync(fileName, arguments, workingDirectory, cancellationToken);
        if (result.ExitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
                ? $"{fileName} terminato con exit code {result.ExitCode}."
                : detail.Trim());
        }

        return string.IsNullOrWhiteSpace(result.StandardOutput) ? result.StandardError : result.StandardOutput;
    }

    private static Version? ParseVersion(string text)
    {
        Match match = Regex.Match(text, @"(\d+)\.(\d+)\.(\d+)");
        return match.Success
            ? new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value))
            : null;
    }

    private static string GetProcessVersionText(LocalProcessResult result)
    {
        return (string.IsNullOrWhiteSpace(result.StandardOutput)
            ? result.StandardError
            : result.StandardOutput).Trim();
    }

    internal static bool IsSupportedOcrPythonVersion(Version version)
    {
        return version.Major == 3 && version.Minor >= 10 && version.Minor <= 13;
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

    private sealed record OcrPythonCommand(string FileName, IReadOnlyList<string> PrefixArguments)
    {
        public string[] WithArguments(IReadOnlyList<string> arguments)
        {
            return [.. PrefixArguments, .. arguments];
        }
    }
}
