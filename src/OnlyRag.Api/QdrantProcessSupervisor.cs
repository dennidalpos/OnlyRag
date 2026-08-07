using System.ComponentModel;
using System.Diagnostics;

namespace OnlyRag.Api;

internal sealed class QdrantProcessSupervisor : IAsyncDisposable
{
    private readonly object processLock = new();
    private WindowsKillOnCloseProcessJob? processJob;
    private Process? startedProcess;
    private int autoHealRestartCount;
    private DateTimeOffset? lastAutoHealedAtUtc;

    public int AutoHealRestartCount => Volatile.Read(ref autoHealRestartCount);
    public DateTimeOffset? LastAutoHealedAtUtc => lastAutoHealedAtUtc;

    public void RecordAutoHeal()
    {
        Interlocked.Increment(ref autoHealRestartCount);
        lastAutoHealedAtUtc = DateTimeOffset.UtcNow;
    }

    public bool IsOwnedProcess(int pid, string? expectedBinaryPath)
    {
        if (!TryOpenOwnedProcess(pid, expectedBinaryPath, out Process? process) || process is null)
        {
            return false;
        }

        using (process)
        {
            return !process.HasExited;
        }
    }

    public bool TryAdoptProcess(int pid, string? expectedBinaryPath)
    {
        if (!TryOpenOwnedProcess(pid, expectedBinaryPath, out Process? process) || process is null)
        {
            return false;
        }

        try
        {
            AttachProcess(process, requireWindowsJob: false);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            process.Dispose();
            return false;
        }
    }

    public void AttachStartedProcess(Process process)
    {
        AttachProcess(process, requireWindowsJob: true);
    }

    private void AttachProcess(Process process, bool requireWindowsJob)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                processJob ??= WindowsKillOnCloseProcessJob.Create();
                processJob.Assign(process);
            }
            catch (Win32Exception) when (!requireWindowsJob)
            {
            }
        }

        lock (processLock)
        {
            Process? previous = startedProcess;
            startedProcess = process;
            if (previous is not null && previous.Id != process.Id)
            {
                previous.Dispose();
            }
        }
    }

    public void DetachStartedProcess(Process process)
    {
        lock (processLock)
        {
            if (startedProcess?.Id == process.Id)
            {
                startedProcess = null;
            }
        }
    }

    public async Task StopAsync(
        int? persistedPid,
        string? expectedBinaryPath,
        CancellationToken cancellationToken = default)
    {
        Process? process = TakeStartedProcess();
        try
        {
            if (process is not null)
            {
                await KillAndDisposeProcessAsync(process, cancellationToken);
            }
            else if (persistedPid is not null
                && TryOpenOwnedProcess(persistedPid.Value, expectedBinaryPath, out Process? persistedProcess)
                && persistedProcess is not null)
            {
                await KillAndDisposeProcessAsync(persistedProcess, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(null, null);
        if (OperatingSystem.IsWindows())
        {
            processJob?.Dispose();
        }

        processJob = null;
    }

    public static void KillAndDisposeProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    private Process? TakeStartedProcess()
    {
        lock (processLock)
        {
            Process? process = startedProcess;
            startedProcess = null;
            return process;
        }
    }

    private static bool TryOpenOwnedProcess(
        int pid,
        string? expectedBinaryPath,
        out Process? process)
    {
        process = null;
        if (string.IsNullOrWhiteSpace(expectedBinaryPath))
        {
            return false;
        }

        try
        {
            Process candidate = Process.GetProcessById(pid);
            if (IsSameExecutable(candidate, expectedBinaryPath))
            {
                process = candidate;
                return true;
            }

            candidate.Dispose();
            return false;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    private static async Task KillAndDisposeProcessAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken);
            }
        }
        finally
        {
            process.Dispose();
        }
    }

    private static bool IsSameExecutable(Process process, string expectedPath)
    {
        try
        {
            string? actualPath = process.MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(actualPath))
            {
                string actualName = Path.GetFileName(actualPath);
                string expectedName = Path.GetFileName(expectedPath);
                if (!string.Equals(actualName, expectedName, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                try
                {
                    if (string.Equals(
                        Path.GetFullPath(actualPath),
                        Path.GetFullPath(expectedPath),
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                }

                return true;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
        }

        try
        {
            string processName = process.ProcessName;
            string expectedNameWithoutExt = Path.GetFileNameWithoutExtension(expectedPath);
            return string.Equals(processName, expectedNameWithoutExt, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return false;
        }
    }
}
