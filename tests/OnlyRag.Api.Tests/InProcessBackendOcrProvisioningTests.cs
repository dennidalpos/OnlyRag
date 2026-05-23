using System.Net;
using System.Net.Http.Json;
using OnlyRag.Api;
using OnlyRag.Core;

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
}
