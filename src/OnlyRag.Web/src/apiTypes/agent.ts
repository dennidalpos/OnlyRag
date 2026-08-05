export type AgentToolCall = {
  callId: string;
  toolName: string;
  argumentsJson: string;
  requiresApproval: boolean;
  explanation?: string | null;
};

export type AgentToolResult = {
  callId: string;
  toolName: string;
  success: boolean;
  output: string;
  error?: string | null;
  diffPatch?: string | null;
};

export type AgentRunRequest = {
  goal: string;
  model?: string | null;
  mode?: "plan" | "write";
  workspaceRoot?: string | null;
  autoApproveCommands?: boolean;
  maxIterations?: number | null;
  resumeRunId?: string | null;
  maxToolCalls?: number | null;
  maxEstimatedTokens?: number | null;
  maxDurationSeconds?: number | null;
  completionCriteria?: AgentCompletionCriterion[] | null;
};

export type AgentCompletionVerificationKind = "Command" | "Tool";

export type AgentCompletionCriterion = {
  id: string;
  description: string;
  verificationKind: AgentCompletionVerificationKind;
  expectedToolName: string;
  expectedCommand?: string | null;
  required?: boolean;
};

export type AgentCompletionVerificationStatus = "Pending" | "Passed" | "Failed";

export type AgentCompletionVerification = {
  criterionId: string;
  status: AgentCompletionVerificationStatus;
  toolCallId: string;
  toolName: string;
  evidence: string;
  verifiedAtUtc: string;
};

export type AgentRunPhase = "Plan" | "Act" | "Observe" | "Verify" | "Recover" | "Finalize" | "Completed" | "Failed" | "Cancelled";

export type AgentStepEvent = {
  type: "thought" | "thought_chunk" | "tool_proposed" | "batch_tools_proposed" | "plan_update" | "plan_updated" | "approval_required" | "tool_result" | "final_response" | "json_parse_warning" | "state_changed" | "error";
  content?: string | null;
  toolCall?: AgentToolCall | null;
  toolResult?: AgentToolResult | null;
  taskId?: string | null;
  batchToolCalls?: AgentToolCall[] | null;
  planMarkdown?: string | null;
  subagentRole?: string | null;
  runId?: string | null;
  phase?: AgentRunPhase | null;
};

export type ApproveToolCallRequest = {
  callId: string;
  approved: boolean;
};

export type BackgroundTaskInfo = {
  taskId: string;
  command: string;
  workingDirectory: string;
  isRunning: boolean;
  exitCode?: number | null;
  startedAt: string;
  finishedAt?: string | null;
};

export type ManageTaskRequest = {
  action: "kill" | "send_input" | "status";
  taskId: string;
  input?: string | null;
};
