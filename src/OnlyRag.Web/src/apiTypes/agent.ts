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
};

export type AgentRunRequest = {
  goal: string;
  model?: string | null;
  mode?: "plan" | "write";
  workspaceRoot?: string | null;
  autoApproveCommands?: boolean;
};

export type AgentStepEvent = {
  type: "thought" | "tool_proposed" | "approval_required" | "tool_result" | "final_response" | "error";
  content?: string | null;
  toolCall?: AgentToolCall | null;
  toolResult?: AgentToolResult | null;
  taskId?: string | null;
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
