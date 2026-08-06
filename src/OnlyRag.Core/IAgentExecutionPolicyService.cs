namespace OnlyRag.Core;

public enum AgentRiskLevel
{
    Low,
    Medium,
    High,
    Critical
}

public sealed record ToolExecutionContext(
    string CallId,
    string ToolName,
    string ArgumentsJson,
    string WorkspaceRoot,
    string? UserRole = null,
    bool RequiresApproval = false);

public sealed record AgentPolicyDecision(
    bool Allowed,
    AgentRiskLevel RiskLevel,
    string? DenialReason,
    bool RequiresUserApproval = false,
    int TimeoutSeconds = 300,
    long MaxOutputSizeBytes = 1048576);

public sealed record AuditLogRecord(
    long Id,
    string CallId,
    string ToolName,
    string RiskLevel,
    bool Allowed,
    string WorkspaceRoot,
    string ArgumentsJson,
    string? OutputOrError,
    DateTimeOffset TimestampUtc);

public interface IAgentExecutionPolicyService
{
    Task<AgentPolicyDecision> EvaluateAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken = default);

    Task PostExecutionVerifyAsync(
        ToolExecutionContext context,
        bool success,
        string output,
        string? error,
        CancellationToken cancellationToken = default);

    Task LogAuditRecordAsync(
        AuditLogRecord record,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditLogRecord>> GetAuditLogsAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);
}
