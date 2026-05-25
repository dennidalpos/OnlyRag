using OnlyRag.Api;

namespace OnlyRag.Api.Tests;

public sealed class OcrProvisionRuntimeResolverTests
{
    [Fact]
    public async Task AutoFallsBackToCpuWithoutNvidia()
    {
        FakeProcessLauncher processLauncher = new();
        OcrProvisionRuntimeResolver resolver = new(processLauncher, _ => null);

        OcrProvisionRuntime runtime = await resolver.ResolveAsync("auto", CancellationToken.None);

        Assert.Equal("cpu", runtime.Target);
        Assert.Equal("requirements-cpu.txt", runtime.RequirementsFileName);
        Assert.Contains("nvidia-smi", runtime.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExplicitNvidiaRequiresNvidiaSmi()
    {
        FakeProcessLauncher processLauncher = new();
        OcrProvisionRuntimeResolver resolver = new(processLauncher, _ => null);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveAsync("nvidia", CancellationToken.None));

        Assert.Contains("nvidia-smi", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("581.01, NVIDIA GeForce RTX 4090, 8.9", "cuda130", "requirements-nvidia-cu130.txt")]
    [InlineData("576.02, NVIDIA GeForce RTX 2070, 7.5", "cuda129", "requirements-nvidia-cu129.txt")]
    [InlineData("561.17, NVIDIA GeForce RTX 4070, 8.9", "cuda126", "requirements-nvidia-cu126.txt")]
    [InlineData("520.06, NVIDIA GeForce GTX 1660, 7.5", "cuda118", "requirements-nvidia-cu118.txt")]
    public async Task SelectsCompatibleNvidiaRuntime(
        string nvidiaSmiOutput,
        string expectedRuntime,
        string expectedRequirements)
    {
        FakeProcessLauncher processLauncher = new()
        {
            RunHandler = (_, _, _) => new LocalProcessResult(0, nvidiaSmiOutput, string.Empty)
        };
        OcrProvisionRuntimeResolver resolver = new(processLauncher, _ => @"C:\Windows\System32\nvidia-smi.exe");

        OcrProvisionRuntime runtime = await resolver.ResolveAsync("auto", CancellationToken.None);

        Assert.Equal("nvidia", runtime.Target);
        Assert.Equal(expectedRuntime, runtime.ResolvedRuntime);
        Assert.Equal(expectedRequirements, runtime.RequirementsFileName);
    }

    [Fact]
    public async Task Series50FallsBackToCpuUntilStableWindowsWheelIsVerified()
    {
        FakeProcessLauncher processLauncher = new()
        {
            RunHandler = (_, _, _) => new LocalProcessResult(0, "581.01, NVIDIA GeForce RTX 5090, 12.0", string.Empty)
        };
        OcrProvisionRuntimeResolver resolver = new(processLauncher, _ => @"C:\Windows\System32\nvidia-smi.exe");

        OcrProvisionRuntime runtime = await resolver.ResolveAsync("auto", CancellationToken.None);

        Assert.Equal("cpu", runtime.Target);
        Assert.Contains("serie 50", runtime.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExplicitNvidiaRejectsSeries50ExperimentalRuntime()
    {
        FakeProcessLauncher processLauncher = new()
        {
            RunHandler = (_, _, _) => new LocalProcessResult(0, "581.01, NVIDIA GeForce RTX 5090, 12.0", string.Empty)
        };
        OcrProvisionRuntimeResolver resolver = new(processLauncher, _ => @"C:\Windows\System32\nvidia-smi.exe");

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveAsync("nvidia", CancellationToken.None));

        Assert.Contains("serie 50", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OldNvidiaDriverFallsBackToCpuForAuto()
    {
        FakeProcessLauncher processLauncher = new()
        {
            RunHandler = (_, _, _) => new LocalProcessResult(0, "519.99, NVIDIA GeForce GTX 1080, 6.1", string.Empty)
        };
        OcrProvisionRuntimeResolver resolver = new(processLauncher, _ => @"C:\Windows\System32\nvidia-smi.exe");

        OcrProvisionRuntime runtime = await resolver.ResolveAsync("auto", CancellationToken.None);

        Assert.Equal("cpu", runtime.Target);
        Assert.Contains("sotto il minimo", runtime.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeProcessLauncher : ILocalProcessLauncher
    {
        public Func<string, IReadOnlyList<string>, string?, LocalProcessResult>? RunHandler { get; init; }

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
            LocalProcessResult result = RunHandler?.Invoke(fileName, arguments, workingDirectory)
                ?? new LocalProcessResult(0, string.Empty, string.Empty);
            return Task.FromResult(result);
        }
    }
}
