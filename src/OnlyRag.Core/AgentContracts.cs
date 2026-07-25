namespace OnlyRag.Core;

public sealed record AgentToolCall(
    string CallId,
    string ToolName,
    string ArgumentsJson,
    bool RequiresApproval = false,
    string? Explanation = null);

public sealed record AgentToolResult(
    string CallId,
    string ToolName,
    bool Success,
    string Output,
    string? Error = null);

public sealed record AgentRunRequest(
    string Goal,
    string? Model = null,
    string? Mode = "write",
    string? WorkspaceRoot = null,
    bool AutoApproveCommands = false,
    int? MaxIterations = null);

public sealed record AgentStepEvent(
    string Type,
    string? Content = null,
    AgentToolCall? ToolCall = null,
    AgentToolResult? ToolResult = null,
    string? TaskId = null);

public sealed record BackgroundTaskInfo(
    string TaskId,
    string Command,
    string WorkingDirectory,
    bool IsRunning,
    int? ExitCode,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);

public sealed record ManageTaskRequest(
    string Action,
    string TaskId,
    string? Input = null);

public sealed record ApproveToolCallRequest(
    string CallId,
    bool Approved);
