using OnlyRag.Core;
using OnlyRag.Infrastructure.Ocr;

namespace OnlyRag.Api;

public sealed class OcrStartupAnalysisService
{
    private const long MinimumOcrProvisionDiskBytes = 2L * 1024L * 1024L * 1024L;

    private readonly ILocalProcessLauncher processLauncher;
    private readonly OcrProvisionRuntimeResolver ocrRuntimeResolver;

    public OcrStartupAnalysisService(ILocalProcessLauncher processLauncher)
    {
        this.processLauncher = processLauncher;
        ocrRuntimeResolver = new OcrProvisionRuntimeResolver(processLauncher);
    }

    public Task<OcrStartupAnalysisResponse> AnalyzeAsync(
        IOcrEngine ocrEngine,
        OcrGpuCapabilityService gpuCapability,
        CancellationToken cancellationToken)
    {
        return AnalyzeAsync(null, ocrEngine, gpuCapability, cancellationToken);
    }

    internal async Task<OcrStartupAnalysisResponse> AnalyzeAsync(
        DiagnosticsProbeCacheService? probeCache,
        IOcrEngine ocrEngine,
        OcrGpuCapabilityService gpuCapability,
        CancellationToken cancellationToken)
    {
        List<string> findings = [];
        bool isWindowsSupported = OperatingSystem.IsWindows();
        if (!isWindowsSupported)
        {
            findings.Add("Automatic OCR configuration is only available on Windows.");
        }

        long availableDiskBytes = GetProvisioningDriveAvailableBytes();
        bool hasMinimumDiskSpace = availableDiskBytes >= MinimumOcrProvisionDiskBytes;
        findings.Add(hasMinimumDiskSpace
            ? "Sufficient disk space for the local OCR runtime."
            : "Insufficient disk space to prepare the local OCR runtime.");

        bool hasCompatiblePython = await HasCompatiblePythonAsync(cancellationToken);
        findings.Add(hasCompatiblePython
            ? "Compatible Python 3.10-3.13 detected."
            : "Python 3.10-3.13 not detected.");

        OcrEngineAvailability availability = probeCache is not null
            ? await probeCache.CheckOcrAvailabilityAsync(ocrEngine, cancellationToken)
            : await ocrEngine.CheckAvailabilityAsync(cancellationToken);
        findings.Add(availability.IsConfigured
            ? $"OCR runtime configured: {availability.EngineName} {availability.EngineVersion}."
            : "Paddle OCR runtime not yet configured.");

        OcrProvisionRuntime runtime = await ResolveRecommendedRuntimeAsync(cancellationToken);
        bool isNvidiaRuntimeAvailable = runtime.IsNvidia;
        findings.Add(runtime.Detail);

        OcrGpuCapabilityResponse gpu = probeCache is not null
            ? await probeCache.CheckOcrGpuCapabilityAsync(gpuCapability, ocrEngine, cancellationToken)
            : await gpuCapability.CheckAsync(ocrEngine, cancellationToken);
        findings.Add(gpu.IsUsable
            ? "GPU PaddleOCR usable."
            : gpu.BlockReason ?? "GPU PaddleOCR unusable.");

        bool canProvision = isWindowsSupported && hasMinimumDiskSpace && hasCompatiblePython;
        bool hasRepairableOcrRuntimeIssue = IsRepairableOcrRuntimeIssue(availability.Message);
        bool shouldPrompt = canProvision && !availability.IsConfigured;
        string recommendedTarget = runtime.IsNvidia
            ? OcrProvisionRuntimeResolver.NvidiaTarget
            : OcrProvisionRuntimeResolver.CpuTarget;
        string title = shouldPrompt
            ? hasRepairableOcrRuntimeIssue
                ? "OCR runtime to repair"
                : runtime.IsNvidia
                    ? "GPU OCR to install"
                    : "CPU OCR to install"
            : availability.IsConfigured
                ? "OCR already configured"
                : "Manual OCR configuration required";
        string message = shouldPrompt
            ? hasRepairableOcrRuntimeIssue && !string.IsNullOrWhiteSpace(availability.Message)
                ? availability.Message
                : "OnlyRag does not yet see a PaddleOCR runtime installed. Press Install OCR to prepare it automatically, or Verify now after manual configuration."
            : availability.IsConfigured
                ? "The local OCR runtime is already available."
                : "Complete the indicated prerequisites, then configure OCR from Settings.";

        return new OcrStartupAnalysisResponse(
            shouldPrompt,
            isWindowsSupported,
            hasMinimumDiskSpace,
            availableDiskBytes,
            MinimumOcrProvisionDiskBytes,
            hasCompatiblePython,
            availability.IsConfigured,
            isNvidiaRuntimeAvailable,
            gpu.IsUsable,
            recommendedTarget,
            title,
            message,
            findings);
    }

    private async Task<bool> HasCompatiblePythonAsync(CancellationToken cancellationToken)
    {
        foreach (OcrPythonCommand candidate in ResolvePythonCandidates())
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

            Version? version = OcrPythonRuntime.ParseVersion(OcrPythonRuntime.GetVersionText(result));
            if (version is not null && OcrPythonRuntime.IsSupportedVersion(version))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<OcrProvisionRuntime> ResolveRecommendedRuntimeAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await ocrRuntimeResolver.ResolveAsync(OcrProvisionRuntimeResolver.AutoTarget, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return OcrProvisionRuntime.Cpu(UserFacingErrorText.FromExternalDetail(
                ex.Message,
                "NVIDIA runtime not detected. CPU runtime will be used."));
        }
    }

    private static IEnumerable<OcrPythonCommand> ResolvePythonCandidates()
    {
        return OcrPythonRuntime.ResolveCandidates(DependencyProvisioningService.ResolveExecutable);
    }

    private static long GetProvisioningDriveAvailableBytes()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string root = Path.GetPathRoot(string.IsNullOrWhiteSpace(localAppData)
            ? Environment.CurrentDirectory
            : localAppData) ?? Path.GetPathRoot(Environment.CurrentDirectory) ?? "C:\\";
        try
        {
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static bool IsRepairableOcrRuntimeIssue(string? message)
    {
        return message?.StartsWith(
            "Local OCR runtime incomplete or damaged.",
            StringComparison.OrdinalIgnoreCase) == true;
    }

}
