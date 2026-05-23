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

    public async Task<OcrStartupAnalysisResponse> AnalyzeAsync(
        IOcrEngine ocrEngine,
        OcrGpuCapabilityService gpuCapability,
        CancellationToken cancellationToken)
    {
        List<string> findings = [];
        bool isWindowsSupported = OperatingSystem.IsWindows();
        if (!isWindowsSupported)
        {
            findings.Add("La configurazione automatica OCR e disponibile solo su Windows.");
        }

        long availableDiskBytes = GetProvisioningDriveAvailableBytes();
        bool hasMinimumDiskSpace = availableDiskBytes >= MinimumOcrProvisionDiskBytes;
        findings.Add(hasMinimumDiskSpace
            ? "Spazio disco sufficiente per il runtime OCR locale."
            : "Spazio disco insufficiente per preparare il runtime OCR locale.");

        bool hasCompatiblePython = await HasCompatiblePythonAsync(cancellationToken);
        findings.Add(hasCompatiblePython
            ? "Python 3.10-3.13 compatibile rilevato."
            : "Python 3.10-3.13 non rilevato.");

        OcrEngineAvailability availability = await ocrEngine.CheckAvailabilityAsync(cancellationToken);
        findings.Add(availability.IsConfigured
            ? $"Runtime OCR configurato: {availability.EngineName} {availability.EngineVersion}."
            : "Runtime OCR Paddle non ancora configurato.");

        OcrProvisionRuntime runtime = await ResolveRecommendedRuntimeAsync(cancellationToken);
        bool isNvidiaRuntimeAvailable = runtime.IsNvidia;
        findings.Add(runtime.Detail);

        OcrGpuCapabilityResponse gpu = await gpuCapability.CheckAsync(ocrEngine, cancellationToken);
        findings.Add(gpu.IsUsable
            ? "PaddleOCR GPU utilizzabile."
            : gpu.BlockReason ?? "PaddleOCR GPU non utilizzabile.");

        bool canProvision = isWindowsSupported && hasMinimumDiskSpace && hasCompatiblePython;
        bool shouldPrompt = canProvision && !availability.IsConfigured;
        string recommendedTarget = isNvidiaRuntimeAvailable
            ? OcrProvisionRuntimeResolver.NvidiaTarget
            : OcrProvisionRuntimeResolver.CpuTarget;
        string title = shouldPrompt
            ? "Configurazione OCR disponibile"
            : availability.IsConfigured
                ? "OCR gia configurato"
                : "Configurazione OCR manuale richiesta";
        string message = shouldPrompt
            ? "OnlyRag puo preparare il runtime PaddleOCR per utente. La configurazione parte solo dopo conferma."
            : availability.IsConfigured
                ? "Il runtime OCR locale e gia disponibile."
                : "Completa i prerequisiti indicati, poi configura OCR dalle Impostazioni.";

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
                "Runtime NVIDIA non rilevato. Verrà usato il runtime CPU."));
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

}
