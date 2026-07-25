using System.ComponentModel;
using System.Diagnostics;
using OnlyRag.Api;

namespace OnlyRag.Api.Tests;

public sealed class QdrantProcessLifetimeTests
{
    [Fact]
    public void WindowsKillOnCloseProcessJob_DisposeTerminatesAssignedProcess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using Process sleeper = Process.Start(new ProcessStartInfo
        {
            FileName = ResolveWindowsPowerShellPath(),
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList =
            {
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-Command",
                "Start-Sleep -Seconds 30"
            }
        }) ?? throw new InvalidOperationException("PowerShell sleep process was not started.");

        using (WindowsKillOnCloseProcessJob job = WindowsKillOnCloseProcessJob.Create())
        {
            job.Assign(sleeper);
        }

        Assert.True(sleeper.WaitForExit(5000));
    }

    [Fact]
    public async Task QdrantProcessSupervisor_DisposeTerminatesAdoptedProcess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using Process sleeper = StartSleepProcess();
        await using QdrantProcessSupervisor supervisor = new();
        string sleeperPath = await WaitForMainModulePathAsync(sleeper);

        Assert.True(supervisor.TryAdoptProcess(sleeper.Id, sleeperPath));

        await supervisor.DisposeAsync();

        bool exited = false;
        try
        {
            exited = sleeper.HasExited || sleeper.WaitForExit(5000);
        }
        catch (InvalidOperationException)
        {
            // Process object was disposed following successful termination.
            exited = true;
        }

        Assert.True(exited);
    }

    private static string ResolveWindowsPowerShellPath()
    {
        string systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string powerShellPath = Path.Combine(
            systemDirectory,
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        return File.Exists(powerShellPath) ? powerShellPath : "powershell.exe";
    }

    private static Process StartSleepProcess()
    {
        return Process.Start(new ProcessStartInfo
        {
            FileName = ResolveWindowsPowerShellPath(),
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList =
            {
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-Command",
                "Start-Sleep -Seconds 30"
            }
        }) ?? throw new InvalidOperationException("PowerShell sleep process was not started.");
    }

    private static async Task<string> WaitForMainModulePathAsync(Process process)
    {
        TimeSpan timeout = TimeSpan.FromSeconds(5);
        Stopwatch stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                string? modulePath = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(modulePath))
                {
                    return modulePath;
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
            }

            await Task.Delay(50);
        }

        throw new InvalidOperationException("PowerShell sleep process executable path was not available.");
    }
}
