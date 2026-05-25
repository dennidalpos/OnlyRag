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

        Assert.True(supervisor.TryAdoptProcess(sleeper.Id, ResolveWindowsPowerShellPath()));

        await supervisor.DisposeAsync();

        Assert.True(sleeper.WaitForExit(5000));
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
}
