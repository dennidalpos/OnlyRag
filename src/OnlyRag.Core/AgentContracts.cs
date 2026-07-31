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
    string? Error = null,
    string? DiffPatch = null);

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
    string? TaskId = null,
    IReadOnlyList<AgentToolCall>? BatchToolCalls = null,
    string? PlanMarkdown = null,
    string? SubagentRole = null);

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

public sealed record AgentEpisodicMemory(
    string SessionId,
    string Goal,
    string Summary,
    IReadOnlyList<string> KeyFacts,
    DateTimeOffset Timestamp,
    float[]? Embedding = null);

public sealed record EntityGraphNode(
    string NodeId,
    string DocumentId,
    string ChunkId,
    string Name,
    string Type,
    string Description);

public sealed record EntityGraphEdge(
    string EdgeId,
    string SourceNodeId,
    string TargetNodeId,
    string RelationType,
    float Weight,
    string ChunkId);

public sealed record GraphRetrievalResult(
    IReadOnlyList<EntityGraphNode> Nodes,
    IReadOnlyList<EntityGraphEdge> Edges,
    IReadOnlyList<string> RelatedChunkIds,
    float RelevanceScore);

public sealed record AgentSkillRecord(
    string SkillId,
    string Name,
    string Category,
    string PatternDescription,
    string SolutionTemplate,
    DateTimeOffset CreatedAtUtc);

