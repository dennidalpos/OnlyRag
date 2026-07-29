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
            return Blocked("non_windows", "Non disponibile", "OCR GPU e supportato solo su Windows.");
        }

        NvidiaHardwareProbe hardwareProbe = await DetectNvidiaHardwareAsync(cancellationToken);
        if (hardwareProbe.Completed && !hardwareProbe.HasNvidiaHardware)
        {
            return new OcrGpuCapabilityResponse(
                false,
                "CPU",
                null,
                "Nessuna GPU NVIDIA rilevata. OCR CPU disponibile.",
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
                "Driver NVIDIA da riparare",
                "GPU NVIDIA rilevata, ma nvidia-smi non e disponibile. Installa o ripara il driver NVIDIA ufficiale, riavvia Windows e riprova.",
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
                "NVIDIA non disponibile",
                UserFacingErrorText.FromExternalDetail(
                    ex.Message,
                    hardwareProbe.HasNvidiaHardware
                        ? "GPU NVIDIA rilevata, ma il driver o nvidia-smi non completano la verifica. Ripara il driver NVIDIA e riavvia Windows."
                        : "Rilevazione NVIDIA non completata. Dettagli tecnici disponibili nei log locali."),
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
            string blockReason = availability.Message ?? "Il runtime OCR GPU non e utilizzabile.";
            return new OcrGpuCapabilityResponse(
                false,
                IsRepairableOcrRuntimeIssue(blockReason) ? "Runtime OCR da riparare" : "Runtime CUDA non utilizzabile",
                blockReason,
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
                "Runtime PaddlePaddle senza CUDA",
                "Il runtime OCR installato non supporta CUDA. Apri Impostazioni > Diagnostica e premi Installa OCR GPU per installare il runtime NVIDIA GPU, oppure usa OCR CPU.",
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
            return new OcrGpuCapabilityResponse(
                false,
                "CUDA non vede GPU",
                "PaddlePaddle non rileva dispositivi CUDA.",
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
            "GPU OCR utilizzabile",
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
        return message.StartsWith("Runtime OCR locale incompleto o danneggiato.", StringComparison.OrdinalIgnoreCase);
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
