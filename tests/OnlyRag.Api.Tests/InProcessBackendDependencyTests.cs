using System.Net;
using System.Net.Http.Json;
using System.Diagnostics;
using OnlyRag.Api;
using OnlyRag.Core;
using OnlyRag.Infrastructure;

namespace OnlyRag.Api.Tests;

public sealed partial class InProcessBackendTests
{
    [Fact]
    public async Task DependencyOllamaStatus_ReturnsInstallCommandAndNetworkHint()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        OllamaInstallStatus? status = await httpClient.GetFromJsonAsync<OllamaInstallStatus>("/api/dependencies/ollama");

        Assert.NotNull(status);
        Assert.Equal("https://ollama.com/download", status.InstallCommand);
        Assert.DoesNotContain("irm", status.InstallCommand, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("iex", status.InstallCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OLLAMA_HOST", status.NetworkAccessHint);
        Assert.Contains("corporate policy", status.NetworkAccessHint);
    }

    [Fact]
    public async Task DependencyOcrStatus_DoesNotExposeDeveloperBootstrapInstruction()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        OcrProvisionStatus? status = await httpClient.GetFromJsonAsync<OcrProvisionStatus>("/api/dependencies/ocr");

        Assert.NotNull(status);
        Assert.DoesNotContain("Bootstrap", status.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bootstrap", status.LastError ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessLaunchingEndpoint_RejectsUnauthenticatedCall()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        FakeProcessLauncher processLauncher = new();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ProcessLauncher = processLauncher });
        using HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/diagnostics/open-logs-folder",
            new ProcessLaunchRequest(true),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(processLauncher.StartedProcesses);
    }

    [Fact]
    public async Task ProcessLaunchingEndpoint_RequiresExplicitConfirmation()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        FakeProcessLauncher processLauncher = new();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ProcessLauncher = processLauncher });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/diagnostics/open-logs-folder",
            new ProcessLaunchRequest(false),
            JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(processLauncher.StartedProcesses);
    }

    [Fact]
    public async Task DependencyOcrProvision_RequiresExplicitConfirmation()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        FakeProcessLauncher processLauncher = new();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ProcessLauncher = processLauncher });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/dependencies/ocr/provision",
            new ProcessLaunchRequest(false),
            JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(processLauncher.StartedProcesses);
    }

    [Fact]
    public async Task DependencyOcrStartupAnalysis_DoesNotStartProvisioning()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        FakeProcessLauncher processLauncher = new();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ProcessLauncher = processLauncher });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        OcrStartupAnalysisResponse? analysis = await httpClient.GetFromJsonAsync<OcrStartupAnalysisResponse>(
            "/api/dependencies/ocr/startup-analysis",
            JsonOptions);

        Assert.NotNull(analysis);
        Assert.Equal(
            0,
            processLauncher.StartedProcesses.Count(process =>
                process.ArgumentList.Any(argument =>
                    argument.Contains("pip", StringComparison.OrdinalIgnoreCase)
                    || argument.Contains("venv", StringComparison.OrdinalIgnoreCase))));
    }

    [Fact]
    public void DependencyOcrProvision_SupportsOnlyPaddleCompatiblePythonVersions()
    {
        Assert.False(DependencyProvisioningService.IsSupportedOcrPythonVersion(new Version(3, 9, 0)));
        Assert.True(DependencyProvisioningService.IsSupportedOcrPythonVersion(new Version(3, 10, 0)));
        Assert.True(DependencyProvisioningService.IsSupportedOcrPythonVersion(new Version(3, 13, 5)));
        Assert.False(DependencyProvisioningService.IsSupportedOcrPythonVersion(new Version(3, 14, 0)));
    }

    [Fact]
    public async Task DependencyOllamaInstall_RequiresExplicitConfirmation()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        FakeProcessLauncher processLauncher = new();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ProcessLauncher = processLauncher });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/dependencies/ollama/install",
            new ProcessLaunchRequest(false),
            JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(processLauncher.StartedProcesses);
    }

    [Fact]
    public async Task DependencyOllamaInstall_OpensManualDownloadPageWithoutRemoteScriptExecution()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        FakeProcessLauncher processLauncher = new();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ProcessLauncher = processLauncher });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/dependencies/ollama/install",
            new ProcessLaunchRequest(true),
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ProcessStartInfo startInfo = Assert.Single(processLauncher.StartedProcesses);
        Assert.Equal("https://ollama.com/download", startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
        Assert.Empty(startInfo.ArgumentList);
    }

    [Fact]
    public async Task DependencyOllamaInstall_ProcessLaunchFailureReturnsManualOfflinePolicyGuidance()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        FakeProcessLauncher processLauncher = new()
        {
            TryStartErrorMessage = "blocked by enterprise policy: internal details"
        };
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ProcessLauncher = processLauncher });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/dependencies/ollama/install",
            new ProcessLaunchRequest(true),
            JsonOptions);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("offline", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("corporate policy", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("internal details", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenLogsFolder_UsesExpectedExplorerDispatch()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        FakeProcessLauncher processLauncher = new();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ProcessLauncher = processLauncher });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/diagnostics/open-logs-folder",
            new ProcessLaunchRequest(true),
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ProcessStartInfo startInfo = Assert.Single(processLauncher.StartedProcesses);
        Assert.EndsWith("explorer.exe", startInfo.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([Path.GetFullPath(tempDescriptor.Descriptor.StoragePaths.LogsDirectory)], startInfo.ArgumentList.ToArray());
        Assert.False(startInfo.UseShellExecute);
    }

    [Fact]
    public async Task AppDataResetEndpoint_RequiresExplicitConfirmation()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/app/reset-on-next-startup",
            new ProcessLaunchRequest(false),
            JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(File.Exists(tempDescriptor.Descriptor.StoragePaths.PendingResetMarkerPath));
    }

    [Fact]
    public async Task AppDataResetEndpoint_WritesPendingResetMarker()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/app/reset-on-next-startup",
            new ProcessLaunchRequest(true),
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(File.Exists(tempDescriptor.Descriptor.StoragePaths.PendingResetMarkerPath));
    }

    [Fact]
    public async Task OpenLogsFolder_DoesNotReturnProcessFailureDetails()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        string sensitivePath = Path.Combine(tempDescriptor.Root, "logs");
        FakeProcessLauncher processLauncher = new()
        {
            TryStartErrorMessage = $"Access denied to {sensitivePath}"
        };
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ProcessLauncher = processLauncher });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/diagnostics/open-logs-folder",
            new ProcessLaunchRequest(true),
            JsonOptions);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain(sensitivePath, body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reference", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LibreOfficeDownload_UsesExpectedUrlDispatch()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        FakeProcessLauncher processLauncher = new();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ProcessLauncher = processLauncher });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/dependencies/libreoffice/open-download",
            new ProcessLaunchRequest(true),
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ProcessStartInfo startInfo = Assert.Single(processLauncher.StartedProcesses);
        Assert.Equal("https://www.libreoffice.org/download/download-libreoffice/", startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
        Assert.Empty(startInfo.ArgumentList);
    }
}
