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
            return Blocked("Non disponibile", "OCR GPU e supportato solo su Windows.");
        }

        OcrProvisionRuntime runtime;
        try
        {
            OcrProvisionRuntimeResolver resolver = new(processLauncher);
            runtime = await resolver.ResolveAsync(OcrProvisionRuntimeResolver.NvidiaTarget, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            return Blocked(
                "NVIDIA non disponibile",
                UserFacingErrorText.FromExternalDetail(
                    ex.Message,
                    "Rilevazione NVIDIA non completata. Dettagli tecnici disponibili nei log locali."));
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
                packageVersions);
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
                packageVersions);
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
                packageVersions);
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
            packageVersions);
    }

    private static OcrGpuCapabilityResponse Blocked(string status, string blockReason)
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
            new Dictionary<string, string>());
    }

    private static bool IsRepairableOcrRuntimeIssue(string message)
    {
        return message.StartsWith("Runtime OCR locale incompleto o danneggiato.", StringComparison.OrdinalIgnoreCase);
    }
}
