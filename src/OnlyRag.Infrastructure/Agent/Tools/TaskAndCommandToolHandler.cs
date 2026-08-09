using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Agent.Tools;

public sealed class TaskAndCommandToolHandler : IToolHandler
{
    private static readonly JsonSerializerOptions s_indentedOptions = new() { WriteIndented = true };
    private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromMinutes(10);
    private readonly BackgroundTaskManager taskManager;

    public TaskAndCommandToolHandler(BackgroundTaskManager taskManager)
    {
        this.taskManager = taskManager;
    }

    public bool CanHandle(string toolName)
    {
        return toolName.ToLowerInvariant() switch
        {
            "run_command" or "manage_task" => true,
            _ => false
        };
    }

    public Task<AgentToolResult> ExecuteAsync(
        string callId,
        string toolName,
        JsonElement args,
        string workspaceRoot,
        Action<AgentStepEvent>? onStep = null,
        CancellationToken cancellationToken = default)
    {
        AgentToolResult res = toolName.ToLowerInvariant() switch
        {
            "run_command" => RunCommand(callId, toolName, args, workspaceRoot),
            "manage_task" => ManageTask(callId, toolName, args),
            _ => new AgentToolResult(callId, toolName, false, string.Empty, $"Tool '{toolName}' not supported by TaskAndCommandToolHandler")
        };
        return Task.FromResult(res);
    }

    private AgentToolResult RunCommand(string callId, string toolName, JsonElement args, string rootPath)
    {
        string commandLine = ToolHelper.GetArgString(args, "commandLine", "command", "cmd", "script")
            ?? throw new ArgumentException("The 'commandLine' parameter is required");

        if (IsGuiFileOpenCommand(commandLine))
        {
            return new AgentToolResult(
                callId,
                toolName,
                false,
                string.Empty,
                "GUI file opening commands ('start', 'explorer', 'notepad', 'code', 'Invoke-Item', etc.) are disabled. " +
                "Do NOT attempt to open files in external GUI applications. Perform CLI builds, tests, and scripts directly inside PowerShell using run_command (e.g. 'dotnet build', 'npm test', 'pwsh .\\scripts\\...').");
        }

        bool isAsync = (args.TryGetProperty("isAsync", out var a) && a.GetBoolean()) ||
                       (args.TryGetProperty("async", out var a2) && a2.GetBoolean());

        if (isAsync)
        {
            var taskInfo = taskManager.StartTask(commandLine, rootPath);
            return new AgentToolResult(callId, toolName, true, $"Command started in background. TaskID: {taskInfo.TaskId}\nUse manage_task to poll status and logs.");
        }

        string shellExe = ResolveShellExecutable();
        string encodedCmd = Convert.ToBase64String(Encoding.Unicode.GetBytes(commandLine));
        var psi = new ProcessStartInfo
        {
            FileName = shellExe,
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedCmd}",
            WorkingDirectory = rootPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            return new AgentToolResult(callId, toolName, false, string.Empty, "Cannot start shell process.");
        }

        int timeoutSeconds = GetTimeoutSeconds(args);
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        var readTask = Task.WhenAll(process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync());

        bool exited = process.WaitForExit((int)timeout.TotalMilliseconds);
        if (!exited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            string timedOutOutput = "Command exceeded the allowed execution timeout.";
            return new AgentToolResult(callId, toolName, false, string.Empty, timedOutOutput);
        }

        Task<string[]> completedRead = readTask;
        Task.WaitAll(completedRead);

        string stdout = completedRead.Result[0];
        string stderr = completedRead.Result[1];

        string combined = string.IsNullOrWhiteSpace(stderr)
            ? stdout
            : $"{stdout}\n[STDERR]\n{stderr}";

        return new AgentToolResult(
            callId,
            toolName,
            process.ExitCode == 0,
            combined,
            process.ExitCode == 0 ? null : $"Process exited with code {process.ExitCode}");
    }

    private static int GetTimeoutSeconds(JsonElement args)
    {
        if (args.TryGetProperty("timeoutSeconds", out var timeoutSecondsProp) && timeoutSecondsProp.ValueKind == JsonValueKind.Number)
        {
            int parsed = timeoutSecondsProp.GetInt32();
            return parsed > 0 ? Math.Min(parsed, 1800) : 600;
        }

        if (args.TryGetProperty("timeout", out var timeoutProp) && timeoutProp.ValueKind == JsonValueKind.Number)
        {
            int parsed = timeoutProp.GetInt32();
            return parsed > 0 ? Math.Min(parsed, 1800) : 600;
        }

        return (int)DefaultCommandTimeout.TotalSeconds;
    }

    private static bool IsGuiFileOpenCommand(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return false;
        string trimmed = commandLine.Trim().ToLowerInvariant();

        return trimmed.StartsWith("start ") ||
               trimmed.StartsWith("start-process ") ||
               trimmed.StartsWith("explorer") ||
               trimmed.StartsWith("notepad") ||
               trimmed.StartsWith("code ") ||
               trimmed.StartsWith("invoke-item ") ||
               trimmed.StartsWith("ii ") ||
               trimmed.StartsWith("open ") ||
               trimmed.Contains("cmd /c start") ||
               trimmed.Contains("cmd.exe /c start");
    }

    private static string ResolveShellExecutable()
    {
        foreach (string candidate in new[] { "pwsh", "pwsh.exe" })
        {
            try
            {
                using var probe = new Process();
                probe.StartInfo = new ProcessStartInfo
                {
                    FileName = candidate,
                    Arguments = "-NoProfile -Command exit 0",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                if (probe.Start())
                {
                    probe.WaitForExit(3000);
                    if (probe.ExitCode == 0) return candidate;
                }
            }
            catch { }
        }
        return "powershell.exe";
    }

    private AgentToolResult ManageTask(string callId, string toolName, JsonElement args)
    {
        string action = ToolHelper.GetArgString(args, "action", "act", "type") ?? "list";
        string taskId = ToolHelper.GetArgString(args, "taskId", "id", "task") ?? "";
        string input = ToolHelper.GetArgString(args, "input", "text") ?? "";

        if (string.Equals(action, "list", StringComparison.OrdinalIgnoreCase))
        {
            var tasks = taskManager.ListTasks();
            string json = JsonSerializer.Serialize(tasks, s_indentedOptions);
            return new AgentToolResult(callId, toolName, true, json);
        }
        else if (string.Equals(action, "kill", StringComparison.OrdinalIgnoreCase))
        {
            bool killed = taskManager.KillTask(taskId);
            return new AgentToolResult(callId, toolName, killed, killed ? $"Task {taskId} terminated." : $"Unable to terminate task {taskId}.");
        }
        else if (string.Equals(action, "send_input", StringComparison.OrdinalIgnoreCase))
        {
            bool sent = taskManager.SendInput(taskId, input);
            return new AgentToolResult(callId, toolName, sent, sent ? $"Input sent to task {taskId}." : $"Unable to send input to task {taskId}.");
        }
        else if (string.Equals(action, "status", StringComparison.OrdinalIgnoreCase))
        {
            var status = taskManager.GetTaskStatusAndLogs(taskId);
            if (status.HasValue)
            {
                string res = $"[STATUS: {status.Value.Info.IsRunning}]\nLOGS:\n{status.Value.Logs}";
                return new AgentToolResult(callId, toolName, true, res);
            }
            return new AgentToolResult(callId, toolName, false, string.Empty, $"Task {taskId} not found.");
        }

        return new AgentToolResult(callId, toolName, false, string.Empty, $"Unknown task action: {action}");
    }
}
