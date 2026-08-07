using OnlyRag.Api;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ocr;
using Xunit;

namespace OnlyRag.Api.Tests;

public sealed class OcrGpuCapabilityServiceTests
{
    private sealed class FakeProcessLauncher : ILocalProcessLauncher
    {
        public bool TryStart(System.Diagnostics.ProcessStartInfo startInfo, out string? errorMessage)
        {
            errorMessage = null;
            return true;
        }

        public Task<LocalProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string? workingDirectory,
            CancellationToken cancellationToken)
        {
            string output = fileName.Contains("nvidia-smi", StringComparison.OrdinalIgnoreCase)
                ? "560.35, NVIDIA GeForce RTX 4080, 8.9"
                : string.Empty;
            return Task.FromResult(new LocalProcessResult(0, output, string.Empty));
        }
    }

    private sealed class TestOcrEngine : IOcrEngine
    {
        private readonly OcrEngineAvailability availability;

        public TestOcrEngine(OcrEngineAvailability availability)
        {
            this.availability = availability;
        }

        public string EngineName => "TestOCR";

        public string EngineVersion => "1.0";

        public string PreprocessVersion => "prep-v1";

        public Task<OcrEngineAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(availability);
        }

        public Task<OcrEngineAvailability> CheckAvailabilityAsync(
            string device,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(availability);
        }

        public Task<OcrPagePreparation> PreparePageAsync(
            OcrPagePreparationRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<OcrPagePreparation>> PreparePageBatchAsync(
            IReadOnlyList<OcrPagePreparationRequest> requests,
            int maxConcurrency = 4,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<OcrPageResult> RecognizeAsync(
            OcrRecognitionRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<OcrPageResult>> RecognizeBatchAsync(
            IReadOnlyList<OcrRecognitionRequest> requests,
            int maxConcurrency = 4,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }

    [Fact]
    public async Task CheckAsync_WhenCompiledWithCudaIsFalse_FormatsReasonWithPaddleVersion()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        FakeProcessLauncher launcher = new();
        OcrGpuCapabilityService service = new(launcher);

        OcrEngineAvailability availability = new(
            IsConfigured: true,
            EngineName: "PaddleOCR",
            EngineVersion: "3.3.1",
            Message: null,
            CompiledWithCuda: false,
            CudaDeviceCount: 0,
            ActiveDevice: "cpu",
            PackageVersions: new Dictionary<string, string>
            {
                ["paddle"] = "3.3.1"
            });

        TestOcrEngine engine = new(availability);
        OcrGpuCapabilityResponse response = await service.CheckAsync(engine);

        Assert.False(response.IsUsable);
        Assert.Equal("PaddlePaddle runtime without CUDA", response.Status);
        Assert.NotNull(response.BlockReason);
        Assert.Contains("paddlepaddle 3.3.1 (CPU build)", response.BlockReason);
    }

    [Fact]
    public async Task CheckAsync_WhenCompiledWithCudaIsFalse_FormatsReasonWithPaddlepaddleCpuVersion()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        FakeProcessLauncher launcher = new();
        OcrGpuCapabilityService service = new(launcher);

        OcrEngineAvailability availability = new(
            IsConfigured: true,
            EngineName: "PaddleOCR",
            EngineVersion: "3.3.1",
            Message: null,
            CompiledWithCuda: false,
            CudaDeviceCount: 0,
            ActiveDevice: "cpu",
            PackageVersions: new Dictionary<string, string>
            {
                ["paddlepaddle"] = "3.3.1"
            });

        TestOcrEngine engine = new(availability);
        OcrGpuCapabilityResponse response = await service.CheckAsync(engine);

        Assert.False(response.IsUsable);
        Assert.Equal("PaddlePaddle runtime without CUDA", response.Status);
        Assert.NotNull(response.BlockReason);
        Assert.Contains("paddlepaddle 3.3.1 (CPU build)", response.BlockReason);
    }
}
