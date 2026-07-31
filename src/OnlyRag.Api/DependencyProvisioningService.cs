using System.Diagnostics;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ocr;

namespace OnlyRag.Api;

public sealed class DependencyProvisioningService
{
    public const string OllamaDownloadUrl = "https://ollama.com/download";
    public const string OllamaInstallCommand = OllamaDownloadUrl;
    public const string OllamaNetworkAccessHint =
        "Install Ollama manually from the official page. If you are offline or a corporate policy blocks external downloads or browsers, download the program from an approved network or ask the IT department. To use an Ollama endpoint from other LAN PCs, configure OLLAMA_HOST in Ollama's settings/environment variables and restart Ollama.";

    private const string LibreOfficeDownloadUrl = "https://www.libreoffice.org/download/download-libreoffice/";
    private static readonly TimeSpan DefaultOcrProvisionTimeout = TimeSpan.FromMinutes(45);
    private const int OcrProvisionStepCount = 8;

    private readonly ILocalProcessLauncher processLauncher;
    private readonly OcrProvisionRuntimeResolver ocrRuntimeResolver;
    private readonly Func<string, string?> executableResolver;
    private readonly string? ocrScriptsRootOverride;
    private readonly string? ocrInstallRootOverride;
    private readonly TimeSpan ocrProvisionTimeout;
    private readonly object syncRoot = new();

    private OcrProvisionStatus lastOcrProvisionStatus = new(
        false,
        false,
        "OCR to install. Use Install OCR to prepare local dependencies automatically.",
        null);
    private Task? ocrProvisionTask;
    private CancellationTokenSource? ocrProvisionCancellation;
    private bool ocrProvisionCancelRequested;
    private DateTimeOffset ocrProvisionDeadlineUtc;
    private DateTimeOffset? ocrProvisionStartedAtUtc;

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
                "Ollama download not opened. Manually open https://ollama.com/download. " +
                "If you are offline or a corporate policy blocks external browsers or downloads, use an approved network or ask the IT department.");
        }

        return new DependencyActionResponse(
            true,
            "Ollama download page opened. Manually install Ollama, start it, then return to OnlyRag and use Verify now.");
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
            throw new InvalidOperationException(errorMessage ?? "Unable to open the LibreOffice download page.");
        }

        return new DependencyActionResponse(true, "LibreOffice download page for PDF export opened.");
    }

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
                DateTimeOffset.UtcNow,
                "available",
                "OCR available",
                OcrProvisionStepCount,
                OcrProvisionStepCount,
                100,
                "success",
                false,
                capability.IsUsable ? "gpu" : "cpu");
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
            ? "OCR to install. Use Install OCR to prepare local dependencies automatically."
            : availability.Message;

        return new OcrProvisionStatus(
            false,
            false,
            message,
            null,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            StepKey: "not-configured",
            StepLabel: "OCR to install",
            StepIndex: 0,
            StepCount: OcrProvisionStepCount,
            ProgressPercent: 0,
            Severity: "warning",
            CanRetry: true);
    }

    public DependencyActionResponse StartOcrProvision(string? runtimeTarget = null)
    {
        string normalizedTarget = OcrProvisionRuntimeResolver.NormalizeTarget(runtimeTarget);
        lock (syncRoot)
        {
            if (ocrProvisionTask is { IsCompleted: false })
            {
                return new DependencyActionResponse(false, "OCR configuration already in progress.");
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            ocrProvisionStartedAtUtc = now;
            lastOcrProvisionStatus = new OcrProvisionStatus(
                false,
                true,
                $"OCR configuration started. Preparation may take several minutes and is automatically stopped after {FormatTimeout(ocrProvisionTimeout)}.",
                null,
                normalizedTarget,
                "resolving",
                "Preparation started: OnlyRag is choosing the compatible OCR runtime.",
                now,
                now,
                "resolve-runtime",
                "OCR runtime selection",
                1,
                OcrProvisionStepCount,
                CalculateProgressPercent(1),
                "running",
                false,
                null);
            ocrProvisionCancellation?.Dispose();
            ocrProvisionCancellation = new CancellationTokenSource(ocrProvisionTimeout);
            ocrProvisionCancelRequested = false;
            ocrProvisionDeadlineUtc = DateTimeOffset.UtcNow.Add(ocrProvisionTimeout);
            CancellationToken token = ocrProvisionCancellation.Token;
            ocrProvisionTask = Task.Run(() => ProvisionOcrAsync(normalizedTarget, token));
        }

        return new DependencyActionResponse(true, "OCR configuration started.");
    }

    public DependencyActionResponse CancelOcrProvision()
    {
        lock (syncRoot)
        {
            if (ocrProvisionTask is not { IsCompleted: false } || ocrProvisionCancellation is null)
            {
                return new DependencyActionResponse(false, "No OCR configuration in progress.");
            }

            ocrProvisionCancelRequested = true;
            lastOcrProvisionStatus = lastOcrProvisionStatus with
            {
                IsRunning = true,
                Message = "OCR configuration cancellation requested. Stopping running processes...",
                LastError = null,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            ocrProvisionCancellation.Cancel();
        }

        return new DependencyActionResponse(true, "OCR configuration cancellation requested.");
    }

    private async Task ProvisionOcrAsync(string runtimeTarget, CancellationToken cancellationToken)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new InvalidOperationException("Automatic OCR configuration is only available on Windows.");
            }

            string scriptsRoot = ResolveOcrScriptsRoot();
            string bridgePath = Path.Combine(scriptsRoot, "paddle_ocr_bridge.py");
            if (!File.Exists(bridgePath))
            {
                throw new InvalidOperationException("Incomplete OCR runtime: bridge not found.");
            }

            OcrProvisionRuntime runtime = await ocrRuntimeResolver.ResolveAsync(runtimeTarget, cancellationToken);
            string requirementsPath = Path.Combine(scriptsRoot, runtime.RequirementsFileName);
            if (!File.Exists(requirementsPath))
            {
                throw new InvalidOperationException(
                    $"Incomplete OCR runtime: {runtime.RequirementsFileName} not found.");
            }

            SetLastOcrStatus(CreateRunningOcrStatus(
                runtimeTarget,
                runtime.ResolvedRuntime,
                "OCR runtime selected.",
                runtime.Detail,
                "resolve-runtime",
                "OCR runtime selection",
                1));

            SetLastOcrStatus(CreateRunningOcrStatus(
                runtimeTarget,
                runtime.ResolvedRuntime,
                "Compatible Python interpreter verification in progress.",
                "OnlyRag is looking for Python 3.10, 3.11, 3.12, or 3.13.",
                "python",
                "Python verification",
                2));
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
                    "OCR Python environment creation in progress.",
                    $"Runtime folder: {installRoot}",
                    "venv",
                    "Environment creation",
                    3));
                await RunProcessAsync(
                    python.FileName,
                    python.WithArguments(["-m", "venv", venvPath]),
                    null,
                    cancellationToken);
            }

            SetLastOcrStatus(CreateRunningOcrStatus(
                runtimeTarget,
                runtime.ResolvedRuntime,
                "OCR pip upgrade in progress.",
                "Short phase before installing PaddleOCR packages.",
                "pip-upgrade",
                "pip upgrade",
                4));
            await RunProcessAsync(venvPython, ["-m", "pip", "install", "--upgrade", "pip", "--disable-pip-version-check"], null, cancellationToken);
            SetLastOcrStatus(CreateRunningOcrStatus(
                runtimeTarget,
                runtime.ResolvedRuntime,
                "Cleaning up old Paddle packages in progress.",
                "OnlyRag removes incompatible Paddle runtimes before reinstalling the correct one.",
                "paddle-clean",
                "Paddle cleanup",
                5));
            await RunPaddlePackageCleanupAsync(venvPython, null, cancellationToken);
            SetLastOcrStatus(CreateRunningOcrStatus(
                runtimeTarget,
                runtime.ResolvedRuntime,
                "Installing PaddleOCR packages in progress.",
                "This phase may take several minutes and depends on network, pip, and available wheels.",
                "paddle-install",
                "PaddleOCR installation",
                6));
            await RunProcessAsync(venvPython, ["-m", "pip", "install", "--upgrade", "-r", requirementsPath, "--disable-pip-version-check"], null, cancellationToken);
            SetLastOcrStatus(CreateRunningOcrStatus(
                runtimeTarget,
                runtime.ResolvedRuntime,
                "Verifying newly installed OCR runtime in progress.",
                runtime.IsNvidia
                    ? "OnlyRag checks that PaddleOCR sees CUDA and the GPU."
                    : "OnlyRag checks that CPU PaddleOCR is importable and ready.",
                "bridge-check",
                "Runtime verification",
                7));
            await RunProcessAsync(venvPython, [bridgePath, "--mode", "check", "--device", runtime.IsNvidia ? "gpu" : "cpu"], null, cancellationToken);

            SetLastOcrStatus(new OcrProvisionStatus(
                true,
                false,
                $"OCR configured correctly with runtime {runtime.ResolvedRuntime}. You can use OCR from document import and document actions.",
                null,
                runtimeTarget,
                runtime.ResolvedRuntime,
                runtime.Detail,
                StepKey: "complete",
                StepLabel: "OCR configured",
                StepIndex: OcrProvisionStepCount,
                StepCount: OcrProvisionStepCount,
                ProgressPercent: 100,
                Severity: "success",
                CanRetry: false,
                SelectedRuntime: runtime.ResolvedRuntime));
        }
        catch (OperationCanceledException)
        {
            SetLastOcrStatus(new OcrProvisionStatus(
                false,
                false,
                WasOcrProvisionTimedOut()
                    ? $"OCR configuration interrupted: maximum time of {FormatTimeout(ocrProvisionTimeout)} reached."
                    : "OCR configuration cancelled.",
                null,
                runtimeTarget,
                "cancelled",
                "Running OCR preparation processes have been stopped. You can repeat Install OCR whenever you want.",
                StepKey: "cancelled",
                StepLabel: "Configuration cancelled",
                StepIndex: 0,
                StepCount: OcrProvisionStepCount,
                ProgressPercent: 0,
                Severity: "warning",
                CanRetry: true,
                SelectedRuntime: null));
        }
        catch (Exception ex)
        {
            string lastError = UserFacingErrorText.FromExternalDetail(
                ex.Message,
                "Technical error during OCR configuration. Details available in local logs.");
            SetLastOcrStatus(new OcrProvisionStatus(
                false,
                false,
                "OCR configuration not completed.",
                lastError,
                runtimeTarget,
                "unknown",
                null,
                StepKey: "failed",
                StepLabel: "Configuration not completed",
                StepIndex: 0,
                StepCount: OcrProvisionStepCount,
                ProgressPercent: 0,
                Severity: "error",
                CanRetry: true,
                SelectedRuntime: null));
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
            ? "Compatible Python not found."
            : $"Incompatible interpreters found: {string.Join(", ", unsupported)}.";
        throw new InvalidOperationException(
            "OCR requires Python 3.10, 3.11, 3.12, or 3.13. " +
            "PaddlePaddle 3.3.1 does not publish Windows wheels for Python 3.14. " +
            $"{detail} Install a compatible Python version for Windows, then repeat Install OCR.");
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
                ? $"{fileName} exited with code {result.ExitCode}."
                : detail.Trim());
        }

        return string.IsNullOrWhiteSpace(result.StandardOutput) ? result.StandardError : result.StandardOutput;
    }

    private async Task<string> RunPaddlePackageCleanupAsync(
        string venvPython,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        string[] arguments = ["-m", "pip", "uninstall", "-y", "paddlepaddle", "paddlepaddle-gpu"];
        LocalProcessResult result = await processLauncher.RunAsync(venvPython, arguments, workingDirectory, cancellationToken);
        string detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        if (result.ExitCode == 0 || IsBenignPaddlePackageCleanupOutput(detail))
        {
            return string.IsNullOrWhiteSpace(result.StandardOutput) ? result.StandardError : result.StandardOutput;
        }

        throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
            ? $"{venvPython} exited with code {result.ExitCode}."
            : detail.Trim());
    }

    private static bool IsBenignPaddlePackageCleanupOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        return output.Contains("Skipping paddlepaddle", StringComparison.OrdinalIgnoreCase)
            && output.Contains("not installed", StringComparison.OrdinalIgnoreCase)
            && !output.Contains("ERROR:", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatTimeout(TimeSpan timeout)
    {
        return timeout.TotalMinutes >= 1
            ? $"{(int)Math.Ceiling(timeout.TotalMinutes)} minutes"
            : $"{(int)Math.Ceiling(timeout.TotalSeconds)} seconds";
    }

    private static int CalculateProgressPercent(int stepIndex)
    {
        return Math.Clamp((int)Math.Round(stepIndex * 100d / OcrProvisionStepCount), 0, 99);
    }

    internal static Version? ParsePythonVersion(string text)
    {
        return OcrPythonRuntime.ParseVersion(text);
    }

    internal static bool IsSupportedOcrPythonVersion(Version version)
    {
        return OcrPythonRuntime.IsSupportedVersion(version);
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
        string? runtimeDetail,
        string stepKey,
        string stepLabel,
        int stepIndex) =>
        new(
            false,
            true,
            message,
            null,
            runtimeTarget,
            resolvedRuntime,
            runtimeDetail,
            ocrProvisionStartedAtUtc,
            DateTimeOffset.UtcNow,
            stepKey,
            stepLabel,
            stepIndex,
            OcrProvisionStepCount,
            CalculateProgressPercent(stepIndex),
            "running",
            false,
            resolvedRuntime);

    private static bool IsTerminalProvisionStatus(OcrProvisionStatus status) =>
        !status.IsRunning
        && (
            !string.IsNullOrWhiteSpace(status.LastError)
            || string.Equals(status.ResolvedRuntime, "cancelled", StringComparison.OrdinalIgnoreCase)
            || status.Message.StartsWith("OCR configuration not completed", StringComparison.OrdinalIgnoreCase)
        );

    private bool WasOcrProvisionTimedOut()
    {
        lock (syncRoot)
        {
            return !ocrProvisionCancelRequested && DateTimeOffset.UtcNow >= ocrProvisionDeadlineUtc;
        }
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
