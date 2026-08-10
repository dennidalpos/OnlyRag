---
name: autonomous-agent-engine
description: Specialized technical skill for the autonomous AI agent engine in OnlyRag. Covers Monte Carlo Tree Search (MCTS) Tree-of-Thought reasoning, durable persistent run state machine (Plan-Act-Observe-Verify-Recover-Finalize), episodic memory recall & auto-learned skill repository, tool safety policies, subagent DAG orchestration, and deterministic verification gates.
---

# Autonomous Agent Engine & Multi-Agent Orchestration Skill

This skill provides operational and technical standards for developing, maintaining, and extending the autonomous agent execution engine in OnlyRag.

## 1. Official Documentation & Technical References

- **Microsoft .NET 10 Task & Channel Concurrent Execution**: [learn.microsoft.com/dotnet/standard/threads/system-threading-channels](https://learn.microsoft.com/en-us/dotnet/standard/threads/system-threading-channels)
- **ASP.NET Core Server-Sent Events (SSE) & SignalR Streaming**: [learn.microsoft.com/aspnet/core/signalr/streaming](https://learn.microsoft.com/en-us/aspnet/core/signalr/streaming)
- **EF Core 10 State & Transaction Management**: [learn.microsoft.com/ef/core/saving/transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions)
- **Microsoft .NET Task Parallel Library**: [learn.microsoft.com/dotnet/standard/parallel-programming/task-parallel-library-tpl](https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/task-parallel-library-tpl)

## 2. Core Architecture & Components

OnlyRag includes a local-first autonomous agent engine:

- **MCTS Tree-of-Thought Engine** (`AgentMctsStateMachine`): Monte Carlo Tree Search branch selection and active state tracking for complex multi-step reasoning.
- **Durable Persistent Run State Machine** (`PersistentAgentRunStateMachine`): Persisted in SQLite schema v11 (`agent_runs`, `agent_run_transitions`). Enforces the strict phase sequence: `Plan` → `Act` → `Observe` → `Verify` → `Recover` → `Finalize`.
- **Episodic Memory & Skill Repository** (`IAgentEpisodicMemoryService`, `IAgentSkillRepository`, `IAgentSkillAutoLearner`): Recalls past session outcomes, success patterns, and automatically extracts reusable skills into SQLite.
- **Subagent DAG Orchestrator** (`SubagentRunner`): Manages specialized subagents with isolated context, typed inputs/outputs, role budgets, and clean cancellation.
- **Deterministic Completion Verification**: `Finalize` and `Completed` phases are blocked unless every required completion criterion has a matching, verified tool execution result. LLM prose or self-reflection claims are never treated as verification evidence.
- **Tool Policy & Sandboxing**: Policy-enforced tool execution with command allowlists/denylists, workspace sandboxing, duration/output limits, and append-only audit logging (`agent_run_trace_events`).

## 2. Key Source Code References

- **Agent Execution Loop**: [`src/OnlyRag.Api/AgentLoopEngine.cs`](file:///d:/GITHUB/OnlyRag/src/OnlyRag.Api/AgentLoopEngine.cs)
- **State Machine & Transitions**: [`src/OnlyRag.Infrastructure/Agent/PersistentAgentRunStateMachine.cs`](file:///d:/GITHUB/OnlyRag/src/OnlyRag.Infrastructure/Agent/PersistentAgentRunStateMachine.cs)
- **MCTS Tree-of-Thought**: [`src/OnlyRag.Infrastructure/Agent/AgentMctsStateMachine.cs`](file:///d:/GITHUB/OnlyRag/src/OnlyRag.Infrastructure/Agent/AgentMctsStateMachine.cs)
- **Episodic Memory**: [`src/OnlyRag.Infrastructure/Agent/Memory/AgentEpisodicMemoryService.cs`](file:///d:/GITHUB/OnlyRag/src/OnlyRag.Infrastructure/Agent/Memory/AgentEpisodicMemoryService.cs)
- **Skill Repository**: [`src/OnlyRag.Infrastructure/Agent/Memory/AgentSkillRepository.cs`](file:///d:/GITHUB/OnlyRag/src/OnlyRag.Infrastructure/Agent/Memory/AgentSkillRepository.cs)
- **Subagent Execution Engine**: [`src/OnlyRag.Api/SubagentRunner.cs`](file:///d:/GITHUB/OnlyRag/src/OnlyRag.Api/SubagentRunner.cs) e [`src/OnlyRag.Infrastructure/Agent/ISubagentRunner.cs`](file:///d:/GITHUB/OnlyRag/src/OnlyRag.Infrastructure/Agent/ISubagentRunner.cs)
- **Evaluation Trace Dataset**: [`docs/agent-evaluation.dataset.json`](file:///d:/GITHUB/OnlyRag/docs/agent-evaluation.dataset.json)

## 3. Persistent Run Recovery API

- `POST /api/agent/run-stream`: Start a new agent run or pass `resumeRunId` to resume an interrupted non-terminal run.
- `GET /api/agent/runs/{runId}`: Retrieve detailed run state, phase transitions, and verification evidence.
- `GET /api/agent/runs/resumable`: List non-terminal runs that can be resumed after application restart.
- `GET /api/agent/runs/{runId}/trace`: Retrieve append-only execution trace events (latencies, token costs, tool results, decisions).

## 4. Development & Verification Rules

1. **Strict Phase Enforcement**: Always route actions through the state machine phases (`Plan`, `Act`, `Observe`, `Verify`, `Recover`, `Finalize`). Never jump directly to `Completed` without passing `Verify`.
2. **Empirical Verification Evidence Only**: Require real build, test, lint, or tool execution outputs before completing runs. Never accept unverified text claims.
3. **Resilience & Recovery**: Ensure agent runs survive application restarts by checkpointing snapshots into SQLite after every tool call.
4. **Tool Safety & Auditability**: Log every tool invocation, observation, and latency metrics to append-only trace events.
