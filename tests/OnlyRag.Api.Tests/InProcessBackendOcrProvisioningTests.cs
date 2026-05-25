using System.Net;
using System.Net.Http.Json;
using OnlyRag.Api;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ocr;

namespace OnlyRag.Api.Tests;

public sealed partial class InProcessBackendTests
{
    [Fact]
    public async Task DependencyOcrProvisionCancel_RequiresExplicitConfirmation()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/dependencies/ocr/cancel",
            new ProcessLaunchRequest(false),
            JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DependencyOcrProvisionCancel_ReturnsRecoverableStatusWhenNothingIsRunning()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage httpResponse = await httpClient.PostAsJsonAsync(
            "/api/dependencies/ocr/cancel",
            new ProcessLaunchRequest(true),
            JsonOptions);
        DependencyActionResponse? response = await httpResponse.Content.ReadFromJsonAsync<DependencyActionResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        Assert.NotNull(response);
        Assert.False(response.Started);
        Assert.Contains("Nessuna", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DependencyOcrProvisionCancel_StopsRunningProvisionProcess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), "OnlyRag.OcrProvision.Tests", Guid.NewGuid().ToString("N"));
        string scriptsRoot = Path.Combine(root, "scripts");
        string installRoot = Path.Combine(root, "ocr-python");
        Directory.CreateDirectory(scriptsRoot);
        File.WriteAllText(Path.Combine(scriptsRoot, "paddle_ocr_bridge.py"), "# test bridge");
        File.WriteAllText(Path.Combine(scriptsRoot, "requirements-cpu.txt"), "# test requirements");

        TaskCompletionSource pipStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource cancellationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeProcessLauncher processLauncher = new()
        {
            RunAsyncHandler = async (_, arguments, _, cancellationToken) =>
            {
                if (arguments.Contains("--version"))
                {
                    return new LocalProcessResult(0, "Python 3.13.0", string.Empty);
                }

                if (arguments.Contains("pip") && arguments.Contains("install"))
                {
                    pipStarted.TrySetResult();
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        cancellationObserved.TrySetResult();
                        throw;
                    }
                }

                return new LocalProcessResult(0, string.Empty, string.Empty);
            }
        };
        DependencyProvisioningService dependencies = new(
            processLauncher,
            executableName => string.Equals(executableName, "python", StringComparison.OrdinalIgnoreCase)
                ? "python.exe"
                : null,
            ocrScriptsRootOverride: scriptsRoot,
            ocrInstallRootOverride: installRoot,
            ocrProvisionTimeout: TimeSpan.FromMinutes(5));

        try
        {
            DependencyActionResponse start = dependencies.StartOcrProvision(OcrProvisionRuntimeResolver.CpuTarget);
            Assert.True(start.Started);

            await pipStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            OcrProvisionStatus runningStatus = await dependencies.GetOcrStatusAsync(
                new UnavailableOcrEngine(),
                new OcrGpuCapabilityService(processLauncher),
                CancellationToken.None);
            Assert.True(runningStatus.IsRunning);
            Assert.True(runningStatus.StepKey is "pip-upgrade" or "paddle-install", runningStatus.StepKey);
            Assert.InRange(runningStatus.ProgressPercent, 1, 99);
            Assert.Equal("running", runningStatus.Severity);

            DependencyActionResponse cancel = dependencies.CancelOcrProvision();

            Assert.True(cancel.Started);
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DependencyOcrProvision_ToleratesMissingPaddlePackagesDuringCleanup()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), "OnlyRag.OcrProvision.Tests", Guid.NewGuid().ToString("N"));
        string scriptsRoot = Path.Combine(root, "scripts");
        string installRoot = Path.Combine(root, "ocr-python");
        string bridgePath = Path.Combine(scriptsRoot, "paddle_ocr_bridge.py");
        string requirementsPath = Path.Combine(scriptsRoot, "requirements-cpu.txt");
        Directory.CreateDirectory(scriptsRoot);
        File.WriteAllText(bridgePath, "# test bridge");
        File.WriteAllText(requirementsPath, "# test requirements");

        TaskCompletionSource bridgeChecked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeProcessLauncher processLauncher = new()
        {
            RunAsyncHandler = (fileName, arguments, _, _) =>
            {
                if (arguments.Contains("--version"))
                {
                    return Task.FromResult(new LocalProcessResult(0, "Python 3.13.0", string.Empty));
                }

                if (arguments.Contains("uninstall"))
                {
                    return Task.FromResult(new LocalProcessResult(
                        1,
                        string.Empty,
                        "WARNING: Skipping paddlepaddle as it is not installed.\nWARNING: Skipping paddlepaddle-gpu as it is not installed."));
                }

                if (arguments.Contains(bridgePath))
                {
                    bridgeChecked.TrySetResult();
                }

                return Task.FromResult(new LocalProcessResult(0, string.Empty, string.Empty));
            }
        };
        DependencyProvisioningService dependencies = new(
            processLauncher,
            executableName => string.Equals(executableName, "python", StringComparison.OrdinalIgnoreCase)
                ? "python.exe"
                : null,
            ocrScriptsRootOverride: scriptsRoot,
            ocrInstallRootOverride: installRoot,
            ocrProvisionTimeout: TimeSpan.FromMinutes(5));

        try
        {
            DependencyActionResponse start = dependencies.StartOcrProvision(OcrProvisionRuntimeResolver.CpuTarget);

            Assert.True(start.Started);
            await bridgeChecked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains(processLauncher.StartedProcesses, process =>
                process.ArgumentList.Contains("install")
                && process.ArgumentList.Contains(requirementsPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
