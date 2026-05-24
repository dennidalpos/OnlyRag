using OnlyRag.Core;
using OnlyRag.Infrastructure.Ocr;

namespace OnlyRag.Api;

public sealed partial class DependencyProvisioningService
{
    private static readonly TimeSpan DefaultOcrProvisionTimeout = TimeSpan.FromMinutes(45);
    private readonly OcrProvisionRuntimeResolver ocrRuntimeResolver;
    private readonly Func<string, string?> executableResolver;
    private readonly string? ocrScriptsRootOverride;
    private readonly string? ocrInstallRootOverride;
    private readonly TimeSpan ocrProvisionTimeout;
    private readonly object syncRoot = new();
    private OcrProvisionStatus lastOcrProvisionStatus = new(
        false,
        false,
        "OCR non configurato. Usa Configura OCR per preparare automaticamente le dipendenze locali.",
        null);
    private Task? ocrProvisionTask;
    private CancellationTokenSource? ocrProvisionCancellation;
    private bool ocrProvisionCancelRequested;
    private DateTimeOffset ocrProvisionDeadlineUtc;
    private DateTimeOffset? ocrProvisionStartedAtUtc;

    public async Task<OcrProvisionStatus> GetOcrStatusAsync(
        IOcrEngine ocrEngine,
        OcrGpuCapabilityService gpuCapability,
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
        OcrGpuCapabilityResponse capability = await gpuCapability.CheckAsync(ocrEngine, cancellationToken);
        OcrProvisionStatus recentStatus;
        lock (syncRoot)
        {
            recentStatus = lastOcrProvisionStatus;
        }

        if (availability.IsConfigured)
        {
            return new OcrProvisionStatus(
                true,
                false,
                $"OCR configurato: {availability.EngineName} {availability.EngineVersion}.",
                null,
                OcrProvisionRuntimeResolver.AutoTarget,
                capability.IsUsable ? "gpu-usable" : "cpu",
                capability.IsUsable
                    ? capability.RuntimeDetail
                    : capability.BlockReason ?? availability.Message,
                recentStatus.StartedAtUtc,
                DateTimeOffset.UtcNow);
        }

        if (IsTerminalProvisionStatus(recentStatus))
        {
            return recentStatus with
            {
                IsConfigured = false,
                IsRunning = false
            };
        }

        string message = string.IsNullOrWhiteSpace(availability.Message)
            || availability.Message.Contains("Bootstrap", StringComparison.OrdinalIgnoreCase)
            ? "OCR non configurato. Usa Configura OCR per preparare automaticamente le dipendenze locali."
            : availability.Message;

        return new OcrProvisionStatus(false, false, message, null, UpdatedAtUtc: DateTimeOffset.UtcNow);
    }

    public DependencyActionResponse StartOcrProvision(string? runtimeTarget = null)
    {
        string normalizedTarget = OcrProvisionRuntimeResolver.NormalizeTarget(runtimeTarget);
        lock (syncRoot)
        {
            if (ocrProvisionTask is { IsCompleted: false })
            {
                return new DependencyActionResponse(false, "Configurazione OCR già in corso.");
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            ocrProvisionStartedAtUtc = now;
            lastOcrProvisionStatus = new OcrProvisionStatus(
                false,
                true,
                $"Configurazione OCR avviata. La preparazione può richiedere diversi minuti e viene fermata automaticamente dopo {FormatTimeout(ocrProvisionTimeout)}.",
                null,
                normalizedTarget,
                "resolving",
                "Preparazione avviata: OnlyRag sta scegliendo il runtime OCR compatibile.",
                now,
                now);
            ocrProvisionCancellation?.Dispose();
            ocrProvisionCancellation = new CancellationTokenSource(ocrProvisionTimeout);
            ocrProvisionCancelRequested = false;
            ocrProvisionDeadlineUtc = DateTimeOffset.UtcNow.Add(ocrProvisionTimeout);
            CancellationToken token = ocrProvisionCancellation.Token;
            ocrProvisionTask = Task.Run(() => ProvisionOcrAsync(normalizedTarget, token));
        }

        return new DependencyActionResponse(true, "Configurazione OCR avviata.");
    }

    public DependencyActionResponse CancelOcrProvision()
    {
        lock (syncRoot)
        {
            if (ocrProvisionTask is not { IsCompleted: false } || ocrProvisionCancellation is null)
            {
                return new DependencyActionResponse(false, "Nessuna configurazione OCR in corso.");
            }

            ocrProvisionCancelRequested = true;
            lastOcrProvisionStatus = lastOcrProvisionStatus with
            {
                IsRunning = true,
                Message = "Annullamento configurazione OCR richiesto. Arresto dei processi in corso...",
                LastError = null,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            ocrProvisionCancellation.Cancel();
        }

        return new DependencyActionResponse(true, "Annullamento configurazione OCR richiesto.");
    }

    private async Task ProvisionOcrAsync(string runtimeTarget, CancellationToken cancellationToken)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new InvalidOperationException("La configurazione automatica OCR è disponibile solo su Windows.");
            }

            string scriptsRoot = ResolveOcrScriptsRoot();
            string bridgePath = Path.Combine(scriptsRoot, "paddle_ocr_bridge.py");
            if (!File.Exists(bridgePath))
            {
                throw new InvalidOperationException("Runtime OCR incompleto: bridge non trovato.");
            }

            OcrProvisionRuntime runtime = await ocrRuntimeResolver.ResolveAsync(runtimeTarget, cancellationToken);
            string requirementsPath = Path.Combine(scriptsRoot, runtime.RequirementsFileName);
            if (!File.Exists(requirementsPath))
            {
                throw new InvalidOperationException(
                    $"Runtime OCR incompleto: {runtime.RequirementsFileName} non trovato.");
            }

            SetLastOcrStatus(CreateRunningOcrStatus(
                runtimeTarget,
                runtime.ResolvedRuntime,
                "Runtime OCR selezionato.",
                runtime.Detail));

            SetLastOcrStatus(CreateRunningOcrStatus(
                runtimeTarget,
                runtime.ResolvedRuntime,
                "Verifica interprete Python compatibile in corso.",
                "OnlyRag cerca Python 3.10, 3.11, 3.12 o 3.13."));
            OcrPythonCommand python = await ResolveOcrPythonCommandAsync(cancellationToken);

            string installRoot = ResolveOcrInstallRoot();
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
                    cancellationToken);
                string venvVersionText = OcrPythonRuntime.GetVersionText(venvVersionResult);
                Version? venvVersion = ParsePythonVersion(venvVersionText);
                if (venvVersionResult.ExitCode != 0 || venvVersion is null || !IsSupportedOcrPythonVersion(venvVersion))
                {
                    Directory.Delete(venvPath, recursive: true);
                    createVenv = true;
                }
            }

            if (createVenv)
            {
                SetLastOcrStatus(CreateRunningOcrStatus(
                    runtimeTarget,
                    runtime.ResolvedRuntime,
                    "Creazione ambiente Python OCR in corso.",
                    $"Cartella runtime: {installRoot}"));
                await RunProcessAsync(
                    python.FileName,
                    python.WithArguments(["-m", "venv", venvPath]),
                    null,
                    cancellationToken);
            }

            SetLastOcrStatus(CreateRunningOcrStatus(
                runtimeTarget,
                runtime.ResolvedRuntime,
                "Aggiornamento pip OCR in corso.",
                "Fase breve prima dell'installazione dei pacchetti PaddleOCR."));
            await RunProcessAsync(venvPython, ["-m", "pip", "install", "--upgrade", "pip", "--disable-pip-version-check"], null, cancellationToken);
            SetLastOcrStatus(CreateRunningOcrStatus(
                runtimeTarget,
                runtime.ResolvedRuntime,
                "Pulizia vecchi pacchetti Paddle in corso.",
                "OnlyRag rimuove runtime Paddle incompatibili prima di reinstallare quello corretto."));
            await RunProcessAsync(venvPython, ["-m", "pip", "uninstall", "-y", "paddlepaddle", "paddlepaddle-gpu"], null, cancellationToken);
            SetLastOcrStatus(CreateRunningOcrStatus(
                runtimeTarget,
                runtime.ResolvedRuntime,
                "Installazione pacchetti PaddleOCR in corso.",
                "Questa fase può durare diversi minuti e dipende da rete, pip e wheel disponibili."));
            await RunProcessAsync(venvPython, ["-m", "pip", "install", "--upgrade", "-r", requirementsPath, "--disable-pip-version-check"], null, cancellationToken);
            SetLastOcrStatus(CreateRunningOcrStatus(
                runtimeTarget,
                runtime.ResolvedRuntime,
                "Verifica runtime OCR appena installato in corso.",
                runtime.IsNvidia
                    ? "OnlyRag controlla che PaddleOCR veda CUDA e la GPU."
                    : "OnlyRag controlla che PaddleOCR CPU sia importabile e pronto."));
            await RunProcessAsync(venvPython, [bridgePath, "--mode", "check", "--device", runtime.IsNvidia ? "gpu" : "cpu"], null, cancellationToken);

            SetLastOcrStatus(new OcrProvisionStatus(
                true,
                false,
                $"OCR configurato correttamente con runtime {runtime.ResolvedRuntime}. Puoi usare l'OCR da importazione documenti e azioni documento.",
                null,
                runtimeTarget,
                runtime.ResolvedRuntime,
                runtime.Detail));
        }
        catch (OperationCanceledException)
        {
            SetLastOcrStatus(new OcrProvisionStatus(
                false,
                false,
                WasOcrProvisionTimedOut()
                    ? $"Configurazione OCR interrotta: tempo massimo di {FormatTimeout(ocrProvisionTimeout)} raggiunto."
                    : "Configurazione OCR annullata.",
                null,
                runtimeTarget,
                "cancelled",
                "I processi di preparazione OCR in corso sono stati arrestati. Puoi ripetere Configura OCR quando vuoi."));
        }
        catch (Exception ex)
        {
            string lastError = UserFacingErrorText.FromExternalDetail(
                ex.Message,
                "Errore tecnico durante la configurazione OCR. Dettagli disponibili nei log locali.");
            SetLastOcrStatus(new OcrProvisionStatus(
                false,
                false,
                "Configurazione OCR non completata.",
                lastError,
                runtimeTarget,
                "unknown",
                null));
        }
        finally
        {
            lock (syncRoot)
            {
                ocrProvisionCancellation?.Dispose();
                ocrProvisionCancellation = null;
                ocrProvisionCancelRequested = false;
            }
        }
    }

    private void SetLastOcrStatus(OcrProvisionStatus status)
    {
        lock (syncRoot)
        {
            lastOcrProvisionStatus = status with
            {
                StartedAtUtc = status.StartedAtUtc ?? ocrProvisionStartedAtUtc,
                UpdatedAtUtc = status.UpdatedAtUtc ?? DateTimeOffset.UtcNow
            };
        }
    }

    private OcrProvisionStatus CreateRunningOcrStatus(
        string runtimeTarget,
        string resolvedRuntime,
        string message,
        string? runtimeDetail) =>
        new(
            false,
            true,
            message,
            null,
            runtimeTarget,
            resolvedRuntime,
            runtimeDetail,
            ocrProvisionStartedAtUtc,
            DateTimeOffset.UtcNow);

    private static bool IsTerminalProvisionStatus(OcrProvisionStatus status) =>
        !status.IsRunning
        && (
            !string.IsNullOrWhiteSpace(status.LastError)
            || string.Equals(status.ResolvedRuntime, "cancelled", StringComparison.OrdinalIgnoreCase)
            || status.Message.StartsWith("Configurazione OCR non completata", StringComparison.OrdinalIgnoreCase)
        );

    private async Task<OcrPythonCommand> ResolveOcrPythonCommandAsync(CancellationToken cancellationToken)
    {
        List<string> unsupported = [];
        foreach (OcrPythonCommand candidate in OcrPythonRuntime.ResolveCandidates(executableResolver))
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

            string versionText = OcrPythonRuntime.GetVersionText(result);
            Version? version = ParsePythonVersion(versionText);
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

    private string ResolveOcrScriptsRoot()
    {
        if (ocrScriptsRootOverride is not null)
        {
            return ocrScriptsRootOverride;
        }

        string outputRoot = Path.Combine(AppContext.BaseDirectory, "scripts", "ocr");
        if (Directory.Exists(outputRoot))
        {
            return outputRoot;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "ocr"));
    }

    private string ResolveOcrInstallRoot()
    {
        if (ocrInstallRootOverride is not null)
        {
            return ocrInstallRootOverride;
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "OnlyRag", "ocr-python");
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

    private bool WasOcrProvisionTimedOut()
    {
        lock (syncRoot)
        {
            return !ocrProvisionCancelRequested && DateTimeOffset.UtcNow >= ocrProvisionDeadlineUtc;
        }
    }

    private static string FormatTimeout(TimeSpan timeout)
    {
        return timeout.TotalMinutes >= 1
            ? $"{(int)Math.Ceiling(timeout.TotalMinutes)} minuti"
            : $"{(int)Math.Ceiling(timeout.TotalSeconds)} secondi";
    }

    internal static Version? ParsePythonVersion(string text)
    {
        return OcrPythonRuntime.ParseVersion(text);
    }

    internal static bool IsSupportedOcrPythonVersion(Version version)
    {
        return OcrPythonRuntime.IsSupportedVersion(version);
    }
}
