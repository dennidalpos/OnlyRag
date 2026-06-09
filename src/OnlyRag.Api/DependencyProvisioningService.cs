using System.Diagnostics;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ocr;

namespace OnlyRag.Api;

public sealed partial class DependencyProvisioningService
{
    public const string OllamaDownloadUrl = "https://ollama.com/download";
    public const string OllamaInstallCommand = OllamaDownloadUrl;
    public const string OllamaNetworkAccessHint =
        "Installa Ollama manualmente dalla pagina ufficiale. Se sei offline o una policy aziendale blocca download o browser esterni, scarica il programma da una rete approvata o chiedi al reparto IT. Per usare un endpoint Ollama da altri PC della LAN, configura OLLAMA_HOST nelle impostazioni/variabili ambiente di Ollama e riavvia Ollama.";

    private const string LibreOfficeDownloadUrl = "https://www.libreoffice.org/download/download-libreoffice/";
    private readonly ILocalProcessLauncher processLauncher;

    public DependencyProvisioningService(ILocalProcessLauncher processLauncher)
        : this(processLauncher, ResolveExecutable, ocrProvisionTimeout: DefaultOcrProvisionTimeout)
    {
    }

    internal DependencyProvisioningService(
        ILocalProcessLauncher processLauncher,
        Func<string, string?> executableResolver,
        string? ocrScriptsRootOverride = null,
        string? ocrInstallRootOverride = null,
        TimeSpan? ocrProvisionTimeout = null)
    {
        this.processLauncher = processLauncher;
        this.executableResolver = executableResolver;
        this.ocrScriptsRootOverride = ocrScriptsRootOverride;
        this.ocrInstallRootOverride = ocrInstallRootOverride;
        this.ocrProvisionTimeout = ocrProvisionTimeout ?? DefaultOcrProvisionTimeout;
        ocrRuntimeResolver = new OcrProvisionRuntimeResolver(processLauncher, executableResolver);
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

        return new DependencyActionResponse(true, "Pagina download LibreOffice per export PDF aperta.");
    }

    internal static string? ResolveExecutable(string executableName)
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
