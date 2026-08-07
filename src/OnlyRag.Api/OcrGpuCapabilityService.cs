using OnlyRag.Core;
using OnlyRag.Infrastructure.Ocr;

namespace OnlyRag.Api;

public sealed class OcrGpuCapabilityService
{
    private readonly ILocalProcessLauncher processLauncher;

    public OcrGpuCapabilityService(ILocalProcessLauncher processLauncher)
    {
        this.processLauncher = processLauncher;
    }

    public async Task<OcrGpuCapabilityResponse> CheckAsync(
        IOcrEngine ocrEngine,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ocrEngine);

        if (!OperatingSystem.IsWindows())
        {
            return Blocked("non_windows", "Unavailable", "GPU OCR is supported only on Windows.");
        }

        NvidiaHardwareProbe hardwareProbe = await DetectNvidiaHardwareAsync(cancellationToken);
        if (hardwareProbe.Completed && !hardwareProbe.HasNvidiaHardware)
        {
            return new OcrGpuCapabilityResponse(
                false,
                "CPU",
                null,
                "No NVIDIA GPU detected. CPU OCR available.",
                null,
                null,
                null,
                null,
                null,
                null,
                new Dictionary<string, string>(),
                "no_nvidia_gpu");
        }

        if (hardwareProbe.Completed && hardwareProbe.HasNvidiaHardware
            && OcrProvisionRuntimeResolver.ResolveExecutable("nvidia-smi") is null)
        {
            return new OcrGpuCapabilityResponse(
                false,
                "NVIDIA driver to repair",
                "NVIDIA GPU detected, but nvidia-smi is unavailable. Install or repair the official NVIDIA driver, restart Windows and try again.",
                null,
                null,
                hardwareProbe.DisplayName,
                null,
                null,
                null,
                null,
                new Dictionary<string, string>(),
                "nvidia_tool_missing");
        }

        OcrProvisionRuntime runtime;
        try
        {
            OcrProvisionRuntimeResolver resolver = new(processLauncher);
            runtime = await resolver.ResolveAsync(OcrProvisionRuntimeResolver.NvidiaTarget, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            return new OcrGpuCapabilityResponse(
                false,
                "NVIDIA unavailable",
                UserFacingErrorText.FromExternalDetail(
                    ex.Message,
                    hardwareProbe.HasNvidiaHardware
                        ? "NVIDIA GPU detected, but the driver or nvidia-smi did not complete verification. Repair the NVIDIA driver and restart Windows."
                        : "NVIDIA detection not completed. Technical details available in local logs."),
                null,
                null,
                hardwareProbe.DisplayName,
                null,
                null,
                null,
                null,
                new Dictionary<string, string>(),
                hardwareProbe.HasNvidiaHardware ? "nvidia_tool_failed" : "runtime_failed");
        }

        OcrEngineAvailability availability = await ocrEngine.CheckAvailabilityAsync("gpu", cancellationToken);
        IReadOnlyDictionary<string, string> packageVersions =
            availability.PackageVersions ?? new Dictionary<string, string>();

        if (!availability.IsConfigured)
        {
            string blockReason = availability.Message ?? "The GPU OCR runtime is not usable.";
            bool isRepairable = IsRepairableOcrRuntimeIssue(blockReason);
            string coherentReason = isRepairable
                ? blockReason
                : FormatNotConfiguredReason(blockReason, runtime.ResolvedRuntime, packageVersions);
            return new OcrGpuCapabilityResponse(
                false,
                isRepairable ? "OCR runtime to repair" : "CUDA runtime unusable",
                coherentReason,
                runtime.Detail,
                availability.EngineVersion,
                runtime.NvidiaName,
                runtime.DriverVersion,
                availability.CompiledWithCuda,
                availability.CudaDeviceCount,
                availability.ActiveDevice,
                packageVersions,
                "runtime_missing");
        }

        if (availability.CompiledWithCuda is false)
        {
            return new OcrGpuCapabilityResponse(
                false,
                "PaddlePaddle runtime without CUDA",
                FormatNoCudaSupportReason(runtime.ResolvedRuntime, packageVersions),
                runtime.Detail,
                availability.EngineVersion,
                runtime.NvidiaName,
                runtime.DriverVersion,
                availability.CompiledWithCuda,
                availability.CudaDeviceCount,
                availability.ActiveDevice,
                packageVersions,
                "runtime_failed");
        }

        if (availability.CudaDeviceCount is int count && count < 1)
        {
            string gpuName = runtime.NvidiaName ?? "the detected NVIDIA GPU";
            return new OcrGpuCapabilityResponse(
                false,
                "CUDA sees no GPUs",
                $"PaddlePaddle ({runtime.ResolvedRuntime}) does not detect any CUDA devices. "
                + $"{gpuName} is visible to Windows but CUDA is not accessible — "
                + "the NVIDIA driver may need to be reinstalled or the system restarted. "
                + "No system CUDA Toolkit is required: the CUDA runtime is bundled in the GPU wheel.",
                runtime.Detail,
                availability.EngineVersion,
                runtime.NvidiaName,
                runtime.DriverVersion,
                availability.CompiledWithCuda,
                availability.CudaDeviceCount,
                availability.ActiveDevice,
                packageVersions,
                "runtime_failed");
        }

        return new OcrGpuCapabilityResponse(
            true,
            "GPU OCR usable",
            null,
            runtime.Detail,
            availability.EngineVersion,
            runtime.NvidiaName,
            runtime.DriverVersion,
            availability.CompiledWithCuda,
            availability.CudaDeviceCount,
            availability.ActiveDevice,
            packageVersions,
            "usable");
    }

    private static OcrGpuCapabilityResponse Blocked(string capabilityStatus, string status, string blockReason)
    {
        return new OcrGpuCapabilityResponse(
            false,
            status,
            blockReason,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            new Dictionary<string, string>(),
            capabilityStatus);
    }

    private static bool IsRepairableOcrRuntimeIssue(string message)
    {
        return message.StartsWith("Local OCR runtime incomplete or damaged.", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatNoCudaSupportReason(
        string resolvedRuntime,
        IReadOnlyDictionary<string, string> packageVersions)
    {
        string installedPackage = packageVersions.TryGetValue("paddlepaddle-gpu", out string? gpuVer)
            && !string.IsNullOrWhiteSpace(gpuVer) && gpuVer != "not-installed"
                ? $"paddlepaddle-gpu {gpuVer}"
                : packageVersions.TryGetValue("paddlepaddle", out string? cpuVer)
                    && !string.IsNullOrWhiteSpace(cpuVer) && cpuVer != "not-installed"
                        ? $"paddlepaddle {cpuVer} (CPU build)"
                        : packageVersions.TryGetValue("paddle", out string? padVer)
                            && !string.IsNullOrWhiteSpace(padVer) && padVer != "not-installed"
                                ? $"paddlepaddle {padVer} (CPU build)"
                                : "a paddlepaddle package";
        return $"The installed OCR runtime ({resolvedRuntime}) does not support CUDA: {installedPackage} is installed "
            + "but it was not compiled with CUDA. "
            + "Open Settings \u003e Diagnostics and press Install GPU OCR to replace it with the NVIDIA GPU build, or switch to CPU OCR.";
    }

    private static string FormatNotConfiguredReason(
        string rawBridgeMessage,
        string resolvedRuntime,
        IReadOnlyDictionary<string, string> packageVersions)
    {
        string? installedPackage = packageVersions.TryGetValue("paddlepaddle-gpu", out string? gpuVer)
            && !string.IsNullOrWhiteSpace(gpuVer) && gpuVer != "not-installed"
                ? $"paddlepaddle-gpu {gpuVer}"
                : packageVersions.TryGetValue("paddlepaddle", out string? cpuVer)
                    && !string.IsNullOrWhiteSpace(cpuVer) && cpuVer != "not-installed"
                        ? $"paddlepaddle {cpuVer}"
                        : packageVersions.TryGetValue("paddle", out string? padVer)
                            && !string.IsNullOrWhiteSpace(padVer) && padVer != "not-installed"
                                ? $"paddlepaddle {padVer}"
                                : null;
        string packageClause = installedPackage is not null
            ? $" (installed: {installedPackage})"
            : string.Empty;
        return $"GPU OCR runtime ({resolvedRuntime}) is not usable{packageClause}. {rawBridgeMessage}"
            .TrimEnd('.') + '.';
    }

    private async Task<NvidiaHardwareProbe> DetectNvidiaHardwareAsync(CancellationToken cancellationToken)
    {
        string? nvidiaSmiPath = OcrProvisionRuntimeResolver.ResolveExecutable("nvidia-smi");
        if (nvidiaSmiPath is not null)
        {
            return new NvidiaHardwareProbe(true, true, null);
        }

        string? shell = OcrProvisionRuntimeResolver.ResolveExecutable("pwsh")
            ?? OcrProvisionRuntimeResolver.ResolveExecutable("powershell");
        if (shell is null)
        {
            return new NvidiaHardwareProbe(false, false, null);
        }

        LocalProcessResult result = await processLauncher.RunAsync(
            shell,
            [
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-Command",
                "Get-CimInstance Win32_VideoController | ForEach-Object { $_.Name }"
            ],
            null,
            cancellationToken);
        if (result.ExitCode != 0)
        {
            return new NvidiaHardwareProbe(false, false, null);
        }

        string? nvidiaName = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => line.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));

        return new NvidiaHardwareProbe(true, nvidiaName is not null, nvidiaName);
    }

    private sealed record NvidiaHardwareProbe(bool Completed, bool HasNvidiaHardware, string? DisplayName);
}
