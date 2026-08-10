using System.Text.Json;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Logging;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Agent;

public sealed class AgentExecutionPolicyService : IAgentExecutionPolicyService
{
    private static readonly HashSet<string> ReadOnlyTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "list_dir", "read_file", "view_file", "grep_search", "git_diff_inspect",
        "query_retrieval_index", "web_search", "plan_task", "reflect_step", "self_reflection"
    };

    private static readonly HashSet<string> MediumRiskTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "write_file", "create_file", "replace_file_content", "multi_replace_file_content",
        "generate_image_onnx", "generate_image", "ast_dependency_graph", "build_ast_graph"
    };

    private static readonly HashSet<string> HighRiskTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "run_command", "manage_task", "invoke_subagent", "spawn_subagent", "execute_subagent_dag"
    };

    private static readonly string[] DangerousCommandTokens =
    [
        "format ", "diskpart", "del /f /s /q c:", "rmdir /s /q c:",
        "remove-item -recurse -force c:", "invoke-expression", "iex ",
        "start ", "explorer", "notepad", "code ", "invoke-item "
    ];

    private readonly SqlitePolicyAuditRepository auditRepository;
    private readonly ILoggingService? logger;

    public AgentExecutionPolicyService(
        SqlitePolicyAuditRepository auditRepository,
        ILoggingService? logger = null)
    {
        this.auditRepository = auditRepository;
        this.logger = logger;
    }

    public async Task<AgentPolicyDecision> EvaluateAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        AgentRiskLevel riskLevel = ClassifyRisk(context.ToolName, context.ArgumentsJson);

        // 1. Sandboxing Check: Ensure workspace tool operates within designated workspace root
        if (!IsToolWorkspaceSafe(context.ToolName, context.ArgumentsJson, context.WorkspaceRoot, out string? sandboxError))
        {
            logger?.LogWarning("AgentPolicy", $"[POLICY DENY] Tool '{context.ToolName}' failed sandbox check: {sandboxError}");
            AgentPolicyDecision denyDecision = new(
                Allowed: false,
                RiskLevel: riskLevel,
                DenialReason: sandboxError);

            await LogAuditRecordAsync(new AuditLogRecord(
                0, context.CallId, context.ToolName, riskLevel.ToString(), false,
                context.WorkspaceRoot, context.ArgumentsJson, sandboxError, DateTimeOffset.UtcNow), cancellationToken);

            return denyDecision;
        }

        // 2. Command Line Denylist Check
        if (string.Equals(context.ToolName, "run_command", StringComparison.OrdinalIgnoreCase))
        {
            if (IsCommandBlacklisted(context.ArgumentsJson, out string? commandError))
            {
                logger?.LogWarning("AgentPolicy", $"[POLICY DENY] Command in '{context.ToolName}' blacklisted: {commandError}");
                AgentPolicyDecision denyDecision = new(
                    Allowed: false,
                    RiskLevel: AgentRiskLevel.Critical,
                    DenialReason: commandError);

                await LogAuditRecordAsync(new AuditLogRecord(
                    0, context.CallId, context.ToolName, AgentRiskLevel.Critical.ToString(), false,
                    context.WorkspaceRoot, context.ArgumentsJson, commandError, DateTimeOffset.UtcNow), cancellationToken);

                return denyDecision;
            }
        }

        // 3. User Approval Requirement for High / Critical Risk tools unless pre-approved
        bool requiresApproval = context.RequiresApproval || riskLevel >= AgentRiskLevel.High;
        if (requiresApproval && !IsPreApproved(context))
        {
            logger?.LogInfo("AgentPolicy", $"[POLICY APPROVAL REQUIRED] Tool '{context.ToolName}' requires user approval (Risk: {riskLevel})");
        }

        AgentPolicyDecision allowedDecision = new(
            Allowed: true,
            RiskLevel: riskLevel,
            DenialReason: null,
            RequiresUserApproval: requiresApproval,
            TimeoutSeconds: riskLevel == AgentRiskLevel.High ? 600 : 300,
            MaxOutputSizeBytes: 2 * 1024 * 1024);

        await LogAuditRecordAsync(new AuditLogRecord(
            0, context.CallId, context.ToolName, riskLevel.ToString(), true,
            context.WorkspaceRoot, context.ArgumentsJson, null, DateTimeOffset.UtcNow), cancellationToken);

        return allowedDecision;
    }

    public async Task PostExecutionVerifyAsync(
        ToolExecutionContext context,
        bool success,
        string output,
        string? error,
        CancellationToken cancellationToken = default)
    {
        string resultSummary = success
            ? $"[SUCCESS] Length: {output?.Length ?? 0} chars"
            : $"[FAIL] Error: {error}";

        logger?.LogDebug("AgentPolicy", $"[POST-VERIFY] Tool '{context.ToolName}' (CallID: {context.CallId}): {resultSummary}");

        AuditLogRecord record = new(
            0,
            context.CallId,
            context.ToolName,
            ClassifyRisk(context.ToolName, context.ArgumentsJson).ToString(),
            success,
            context.WorkspaceRoot,
            context.ArgumentsJson,
            resultSummary,
            DateTimeOffset.UtcNow);

        await LogAuditRecordAsync(record, cancellationToken);
    }

    public Task LogAuditRecordAsync(AuditLogRecord record, CancellationToken cancellationToken = default)
    {
        return auditRepository.LogRecordAsync(record, cancellationToken);
    }

    public Task<IReadOnlyList<AuditLogRecord>> GetAuditLogsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return auditRepository.GetAuditLogsAsync(limit, cancellationToken);
    }

    private static AgentRiskLevel ClassifyRisk(string toolName, string argumentsJson)
    {
        if (ReadOnlyTools.Contains(toolName))
            return AgentRiskLevel.Low;

        if (MediumRiskTools.Contains(toolName))
            return AgentRiskLevel.Medium;

        if (HighRiskTools.Contains(toolName))
        {
            if (string.Equals(toolName, "run_command", StringComparison.OrdinalIgnoreCase) &&
                IsCommandBlacklisted(argumentsJson, out _))
            {
                return AgentRiskLevel.Critical;
            }
            return AgentRiskLevel.High;
        }

        return AgentRiskLevel.Medium;
    }

    private static bool IsToolWorkspaceSafe(
        string toolName,
        string argumentsJson,
        string workspaceRoot,
        out string? sandboxError)
    {
        sandboxError = null;
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return true; // No workspace bound for global non-file tools
        }

        try
        {
            string fullWorkspaceRoot = Path.GetFullPath(workspaceRoot);

            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            var root = doc.RootElement;

            if (root.ValueKind is not JsonValueKind.Object)
            {
                sandboxError = "Tool arguments must be a JSON object.";
                return false;
            }

            if (!ValidatePathValues(root, fullWorkspaceRoot, workspaceRoot, out sandboxError))
            {
                return false;
            }
        }
        catch (JsonException ex)
        {
            sandboxError = $"Invalid JSON parameters: {ex.Message}";
            return false;
        }
        catch (ArgumentException ex)
        {
            sandboxError = $"Invalid path format: {ex.Message}";
            return false;
        }

        return true;
    }

    private static bool IsCommandBlacklisted(string argumentsJson, out string? commandError)
    {
        commandError = null;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            if (doc.RootElement.ValueKind is not JsonValueKind.Object)
            {
                commandError = "Tool arguments must be a JSON object.";
                return true;
            }

            JsonProperty? commandProperty = null;
            foreach (JsonProperty property in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "commandLine", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(property.Name, "command", StringComparison.OrdinalIgnoreCase))
                {
                    commandProperty = property;
                    break;
                }
            }

            if (commandProperty is null || commandProperty.Value.Value.ValueKind is not JsonValueKind.String)
            {
                commandError = "A string 'commandLine' parameter is required.";
                return true;
            }

            string cmdText = commandProperty.Value.Value.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(cmdText))
            {
                commandError = "A non-empty 'commandLine' parameter is required.";
                return true;
            }

            string lower = cmdText.ToLowerInvariant();
            foreach (string token in DangerousCommandTokens)
            {
                if (lower.Contains(token))
                {
                    commandError = $"Command contains forbidden execution token: '{token}'";
                    return true;
                }
            }

            if (ContainsShellMetacharacters(cmdText))
            {
                commandError = "Command contains shell metacharacters that are not permitted in this sandbox.";
                return true;
            }
        }
        catch (JsonException ex)
        {
            commandError = $"Invalid JSON parameters: {ex.Message}";
            return true;
        }

        return false;
    }

    private static bool ValidatePathValues(
        JsonElement element,
        string workspaceRoot,
        string originalWorkspaceRoot,
        out string? sandboxError,
        string? propertyName = null)
    {
        sandboxError = null;
        if (element.ValueKind == JsonValueKind.String &&
            propertyName is not null &&
            IsPathProperty(propertyName))
        {
            string pathValue = element.GetString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(pathValue))
            {
                string fullPath = Path.IsPathRooted(pathValue)
                    ? Path.GetFullPath(pathValue)
                    : Path.GetFullPath(Path.Combine(workspaceRoot, pathValue));

                if (!IsPathWithinRoot(workspaceRoot, fullPath))
                {
                    sandboxError = $"Path '{pathValue}' is outside the authorized workspace sandbox '{originalWorkspaceRoot}'.";
                    return false;
                }
            }

            return true;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!ValidatePathValues(property.Value, workspaceRoot, originalWorkspaceRoot, out sandboxError, property.Name))
                {
                    return false;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (!ValidatePathValues(item, workspaceRoot, originalWorkspaceRoot, out sandboxError, propertyName))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsPathProperty(string propertyName)
    {
        string nameLower = propertyName.ToLowerInvariant();
        return nameLower.Contains("path") ||
               nameLower.Contains("file") ||
               nameLower.Contains("directory") ||
               nameLower.Contains("folder");
    }

    private static bool ContainsShellMetacharacters(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return false;
        }

        return commandText.IndexOfAny([';', '&', '|', '>', '<', '`']) >= 0;
    }

    private static bool IsPathWithinRoot(string rootPath, string candidatePath)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        string normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));

        if (string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        char boundaryChar = normalizedCandidate[normalizedRoot.Length];
        return boundaryChar == Path.DirectorySeparatorChar || boundaryChar == Path.AltDirectorySeparatorChar;
    }

    private static bool IsPreApproved(ToolExecutionContext context)
    {
        // Internal automated runs or CLI callers marked as approved
        return string.Equals(context.UserRole, "SystemAuto", StringComparison.OrdinalIgnoreCase);
    }
}
