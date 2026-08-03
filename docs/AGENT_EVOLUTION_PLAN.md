# OnlyRag — Autonomous Agent Evolution Plan (SOTA Roadmap)

This document details the incremental architectural plan to elevate OnlyRag's reasoning, retrieval, and execution capabilities to top-tier autonomous agent standards.

---

## Evolution Milestones & Actionable Steps

### Phase 1: MCTS & Tree-Search Reasoning Engine
- **1.1 MCTS Rollouts & Snapshot Rollback Integration**: Integrate `AgentMctsStateMachine` and `WorkspaceSnapshotCheckpointManager` into `AgentLoopEngine.cs` to evaluate alternative tool action branches and automatically rollback workspace state if verification fails.
- **1.2 Dynamic ReAct Critique & ToT Branching**: Add a pre-action Critique step evaluating side effects on code dependencies prior to executing workspace mutations.

### Phase 2: AST Knowledge Graph & HyDE RAG 2.0 Enhancements
- **2.1 Multi-Hop Symbol & Call-Graph Indexing**: Expand `GraphRagAstSymbolIndexer.cs` and `SqliteGraphRetrievalService.cs` to parse and index C#, TypeScript, and Python AST symbols, interfaces, and call graphs.
- **2.2 HyDE Query Expansion**: Implement Hypothetical Document Embeddings (HyDE) in `IQueryTransformationService` to generate hypothetical code snippets before vector search.

### Phase 3: Episodic Memory Retrieval & Procedural Skill Learning
- **3.1 Pre-Task Episodic Vector Search**: Interrogate `SqliteQdrantEpisodicMemoryService` during goal enrichment to retrieve past successful sessions and inject learned key facts into the context.
- **3.2 Automatic Skill Synthesis**: Refine `AgentSkillAutoLearner.cs` to extract verified multi-file refactoring patterns and update project skills (`skills/`).

### Phase 4: Role-Specialized Subagent Swarms
- **4.1 Domain-Specialized Subagent Prompts**: Add specialized subagent roles (*Architect*, *Research*, *Code Refactor*, *Test & Verification*) to `SubagentRunner.cs`.
- **4.2 Isolated Workspace Diff Merging**: Support isolated branch execution and automatic patch merging for concurrent subagents.

### Phase 5: AST Structural Refactoring & Test-Driven Verification Loop
- **5.1 AST Symbol Refactoring Tool**: Implement `ast_structural_refactor` tool using Roslyn and TypeScript compiler APIs for type-safe symbol renaming across the codebase.
- **5.2 Mandatory Test-Driven Rollback Loop**: Enforce automatic execution of `Lint-Code.ps1` and unit tests after code modifications with automatic rollback on test failure.

### Phase 6: Constrained Grammar Decoding & Ollama Inference Tuning
- **6.1 Strict JSON Schema Constrained Decoding**: Pass strict JSON schemas to Ollama API to guarantee 100% compliant tool call JSON formatting at decoding time.
- **6.2 High-Context Performance Tuning**: Optimize `PerformanceSettingsService.cs` for 16k–32k context budgeting and GPU acceleration presets.

---

## Verification & Execution Protocol

For each task:
1. Implement the minimal required change.
2. Run relevant unit/integration tests (`pwsh .\scripts\Test-Code.ps1`).
3. Run static analysis & formatting (`pwsh .\scripts\Lint-Code.ps1`, `pwsh .\scripts\Format-Code.ps1`).
4. Validate with canonical release gate (`pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release`).
5. Update `PROJECT_STATUS.json` by removing the completed todo.
