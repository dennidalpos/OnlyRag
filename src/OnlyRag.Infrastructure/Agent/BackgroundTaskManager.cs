using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Agent;

public sealed class BackgroundTaskManager
{
    private readonly ConcurrentDictionary<string, TaskEntry> tasks = new();

    public BackgroundTaskInfo StartTask(string command, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("Il comando non puo essere vuoto.", nameof(command));
        }

        string taskId = $"task_{Guid.NewGuid():N}"[..12];
        string shellExecutable = ResolveShellExecutable();
        string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));

        var psi = new ProcessStartInfo
        {
            FileName = shellExecutable,
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };

        var process = new Process { StartInfo = psi };
        var logBuffer = new StringBuilder();

        var entry = new TaskEntry(
            TaskId: taskId,
            Command: command,
            WorkingDirectory: workingDirectory,
            Process: process,
            LogBuffer: logBuffer,
            StartedAt: DateTimeOffset.UtcNow);

        tasks[taskId] = entry;

        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                lock (logBuffer)
                {
                    logBuffer.AppendLine(e.Data);
                }
            }
        };

        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                lock (logBuffer)
                {
                    logBuffer.AppendLine($"[STDERR] {e.Data}");
                }
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        _ = Task.Run(async () =>
        {
            await process.WaitForExitAsync();
            entry.FinishedAt = DateTimeOffset.UtcNow;
            entry.ExitCode = process.ExitCode;
        });

        return entry.ToInfo();
    }

    public IReadOnlyList<BackgroundTaskInfo> ListTasks()
    {
        return tasks.Values.Select(t => t.ToInfo()).OrderByDescending(t => t.StartedAt).ToList();
    }

    public (BackgroundTaskInfo Info, string Logs)? GetTaskStatusAndLogs(string taskId)
    {
        if (!tasks.TryGetValue(taskId, out var entry))
        {
            return null;
        }

        string logs;
        lock (entry.LogBuffer)
        {
            logs = entry.LogBuffer.ToString();
        }

        return (entry.ToInfo(), logs);
    }

    public bool SendInput(string taskId, string input)
    {
        if (!tasks.TryGetValue(taskId, out var entry) || entry.Process.HasExited)
        {
            return false;
        }

        try
        {
            entry.Process.StandardInput.WriteLine(input);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool KillTask(string taskId)
    {
        if (!tasks.TryGetValue(taskId, out var entry) || entry.Process.HasExited)
        {
            return false;
        }

        try
        {
            entry.Process.Kill(entireProcessTree: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveShellExecutable()
    {
        // Try pwsh first, fallback to powershell
        string[] candidates = { "pwsh.exe", "powershell.exe" };
        foreach (var candidate in candidates)
        {
            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
            foreach (var path in paths)
            {
                string full = Path.Combine(path, candidate);
                if (File.Exists(full))
                {
                    return candidate;
                }
            }
        }

        return "powershell.exe";
    }

    private sealed class TaskEntry
    {
        public string TaskId { get; }
        public string Command { get; }
        public string WorkingDirectory { get; }
        public Process Process { get; }
        public StringBuilder LogBuffer { get; }
        public DateTimeOffset StartedAt { get; }
        public DateTimeOffset? FinishedAt { get; set; }
        public int? ExitCode { get; set; }

        public TaskEntry(
            string TaskId,
            string Command,
            string WorkingDirectory,
            Process Process,
            StringBuilder LogBuffer,
            DateTimeOffset StartedAt)
        {
            this.TaskId = TaskId;
            this.Command = Command;
            this.WorkingDirectory = WorkingDirectory;
            this.Process = Process;
            this.LogBuffer = LogBuffer;
            this.StartedAt = StartedAt;
        }

        public BackgroundTaskInfo ToInfo()
        {
            bool isRunning = !Process.HasExited;
            return new BackgroundTaskInfo(
                TaskId: TaskId,
                Command: Command,
                WorkingDirectory: WorkingDirectory,
                IsRunning: isRunning,
                ExitCode: isRunning ? null : (ExitCode ?? Process.ExitCode),
                StartedAt: StartedAt,
                FinishedAt: FinishedAt);
        }
    }
}
