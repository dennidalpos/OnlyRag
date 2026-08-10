---
name: onlyrag
description: Primary development skill for OnlyRag. Covers overall architecture, WPF/WebView2 backend bridge, PowerShell automation scripts, local data paths, release verification gates, and code conventions.
---

# OnlyRag — Primary Project Skill

This skill provides comprehensive operational and architectural guidance for developing, testing, packaging, and verifying the OnlyRag application.

## 1. Documentazione Ufficiale & Riferimenti

### Fonti Ufficiali Platform & Framework
- **Microsoft .NET 10 & C# 13 Documentation**: [learn.microsoft.com/dotnet](https://learn.microsoft.com/en-us/dotnet/)
- **ASP.NET Core Minimal APIs**: [learn.microsoft.com/aspnet/core/fundamentals/minimal-apis](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis)
- **Microsoft Edge WebView2**: [learn.microsoft.com/microsoft-edge/webview2](https://learn.microsoft.com/en-us/microsoft-edge/webview2/)
- **SQLite FTS5 Extension**: [sqlite.org/fts5.html](https://www.sqlite.org/fts5.html)
- **Qdrant Vector Database**: [qdrant.tech/documentation](https://qdrant.tech/documentation/)
- **React 19 Documentation**: [react.dev](https://react.dev/)
- **Vite Build Tool**: [vite.dev/guide](https://vite.dev/guide/)
- **ONNX Runtime DirectML**: [learn.microsoft.com/windows/ai/directml](https://learn.microsoft.com/en-us/windows/ai/directml/)
- **NSIS Installer Documentation**: [nsis.sourceforge.io/Docs/](https://nsis.sourceforge.io/Docs/)

### Documentazione Interna OnlyRag
- **Indice Documentazione OnlyRag**: [docs/README.md](file:///d:/GITHUB/OnlyRag/docs/README.md)
- **Architettura**: [docs/ARCHITECTURE.md](file:///d:/GITHUB/OnlyRag/docs/ARCHITECTURE.md)
- **Operazioni & Handoff**: [docs/OPERATIONS.md](file:///d:/GITHUB/OnlyRag/docs/OPERATIONS.md)
- **Flusso Applicazione**: [docs/APP_FLOW.md](file:///d:/GITHUB/OnlyRag/docs/APP_FLOW.md)
- **Inventario Script**: [scripts/README.md](file:///d:/GITHUB/OnlyRag/scripts/README.md)
- **Pipeline RAG & Knowledge Graph**: [docs/RAG_PIPELINE.md](file:///d:/GITHUB/OnlyRag/docs/RAG_PIPELINE.md)
- **Motore Agenti Autonomi**: [docs/AGENT_ENGINE.md](file:///d:/GITHUB/OnlyRag/docs/AGENT_ENGINE.md)
- **Pipeline OCR**: [docs/OCR_PIPELINE.md](file:///d:/GITHUB/OnlyRag/docs/OCR_PIPELINE.md)
- **Generazione Immagini**: [docs/IMAGE_GENERATION.md](file:///d:/GITHUB/OnlyRag/docs/IMAGE_GENERATION.md)
- **Pipeline Traduzione**: [docs/TRANSLATION_PIPELINE.md](file:///d:/GITHUB/OnlyRag/docs/TRANSLATION_PIPELINE.md)
- **Packaging NSIS**: [packaging/README.md](file:///d:/GITHUB/OnlyRag/packaging/README.md)
- **Firma Digitale**: [docs/SIGNING.md](file:///d:/GITHUB/OnlyRag/docs/SIGNING.md)


## 2. Core Architecture

OnlyRag is a local-first Windows desktop app combining:
1. **WPF Desktop Shell** ([`src/OnlyRag.App`](file:///d:/GITHUB/OnlyRag/src/OnlyRag.App)): Hosts Microsoft Edge WebView2 and manages backend startup/shutdown.
2. **React/Vite Web UI** ([`src/OnlyRag.Web`](file:///d:/GITHUB/OnlyRag/src/OnlyRag.Web)): Modern single-page app interface executing inside WebView2.
3. **In-Process Backend API** ([`src/OnlyRag.Api`](file:///d:/GITHUB/OnlyRag/src/OnlyRag.Api)): ASP.NET Core Minimal API hosted in-process inside the WPF app.
4. **Core Contracts** ([`src/OnlyRag.Core`](file:///d:/GITHUB/OnlyRag/src/OnlyRag.Core)): Shared DTOs, interfaces, and settings models.
5. **Infrastructure Adapters** ([`src/OnlyRag.Infrastructure`](file:///d:/GITHUB/OnlyRag/src/OnlyRag.Infrastructure)): SQLite storage (schema v11), Qdrant vector retrieval, Ollama integration, six-stage RAG retrieval (Parent-Child chunking, ONNX Cross-Encoder re-ranking, Query Transformation & Ollama LLM Query Expansion, CRAG confidence evaluation), Subagent Runner execution engine, ONNX image generation, PaddleOCR bridge, and LibreOffice PDF export.
6. **Worker Queue** ([`src/OnlyRag.Worker`](file:///d:/GITHUB/OnlyRag/src/OnlyRag.Worker)): In-process task queue for asynchronous background jobs.


## 3. Directory & Environment Standards

- **Local App Data**: `%LOCALAPPDATA%\OnlyRag` (documents, SQLite database, Qdrant vectors, OCR cache, jobs, settings, logs).
- **Image Models Directory**: `%LOCALAPPDATA%\OnlyRag\models\images`.
- **Installed Target Path**: `%LOCALAPPDATA%\Programs\OnlyRag`.
- **PowerShell Version**: PowerShell 7 (`pwsh`). Run commands from the repository root.

Archive ingestion is implemented for ZIP, TAR, and 7Z. `ArchiveExtractionService` validates
entry paths and decompressed-size limits while streaming entries; supported text, OpenXML, and
text-based PDF entries become pages of the container document. The SQLite schema persists one
`archive_manifest_entries` row per archive entry with provenance, declared/actual size, SHA-256,
status, error, and page/chunk counts. Repeated paths are retained as `Duplicate` entries and are
not indexed twice. The manifest is available through
`GET /api/documents/{id}/archive-manifest`. Image entries (.png, .jpg, .jpeg, .bmp, .gif, .tif, .tiff, .webp) are processed automatically via OCR and indexed into the container document.

The coding agent persists durable runs in SQLite. `agent_runs` stores the goal,
conversation snapshot, current state, time/token/tool budgets, typed completion criteria, and
runtime-observed verification evidence; `agent_run_transitions` records
the validated lifecycle (`Plan`, `Act`, `Observe`, `Verify`, `Recover`, `Finalize`). Resume a
non-terminal run by passing `resumeRunId` to `POST /api/agent/run-stream`; inspect recovery state
with `GET /api/agent/runs/{runId}` or `GET /api/agent/runs/resumable`.

The runtime refuses `Finalize` and `Completed` unless every required completion criterion has a
positive matching tool result. LLM prose and `reflect_step` messages are not verification evidence.

Append-only `agent_run_trace_events` support evaluation. It records model and
tool latency, decisions, observations, errors, token/tool cost, evidence and outcome. Inspect a run
with `GET /api/agent/runs/{runId}/trace`; the reproducible task dataset is
`docs/agent-evaluation.dataset.json`.

### Code Maintenance & Quality Commands
```powershell
# Format code (.NET + Web Prettier)
pwsh .\scripts\Format-Code.ps1

# Static analysis and linting (TypeScript + ESLint + .NET analyzers)
pwsh .\scripts\Lint-Code.ps1

# Run agent-optimized fast test suite (compact PASS/FAIL summary)
pwsh .\scripts\test-agent.ps1

# Run fast unit test suite with compact AI summary (.NET xUnit + Vitest unit tests)
pwsh .\scripts\Test-Code.ps1 -Fast

# Run full integration test suite with compact output
pwsh .\scripts\Test-Code.ps1 -IncludeIntegration

# Run test suite with full verbose developer output
pwsh .\scripts\Test-Code.ps1 -VerboseOutput
```

## 5. Project Skills Inventory (`skills/`)

The repository includes porting skills checked into `skills/`:
- [`skills/onlyrag`](file:///d:/GITHUB/OnlyRag/skills/onlyrag/SKILL.md): Primary architecture, workflows, local data paths, and gate check.
- [`skills/dotnet-wpf-minimal-api`](file:///d:/GITHUB/OnlyRag/skills/dotnet-wpf-minimal-api/SKILL.md): C# .NET 10 WPF host shell, Minimal API backend, WebView2 interop, SQLite.
- [`skills/react-vite-frontend`](file:///d:/GITHUB/OnlyRag/skills/react-vite-frontend/SKILL.md): React 19, Vite, TypeScript, CSS custom properties, Lucide icons, Vitest, Playwright.
- [`skills/rag-vector-retrieval`](file:///d:/GITHUB/OnlyRag/skills/rag-vector-retrieval/SKILL.md): Dual-Tier chunking, SQLite FTS5, Qdrant, Heuristic Re-ranking, CRAG evaluation.
- [`skills/autonomous-agent-engine`](file:///d:/GITHUB/OnlyRag/skills/autonomous-agent-engine/SKILL.md): MCTS Tree-of-Thought, Plan-Act-Observe-Verify state machine, Episodic memory, Subagent DAG.
- [`skills/onnx-directml-image-gen`](file:///d:/GITHUB/OnlyRag/skills/onnx-directml-image-gen/SKILL.md): ONNX DirectML GPU / CPU fallback, SDXL/LCM models, SHA256 integrity, Canvas editor.
- [`skills/windows-packaging-signing`](file:///d:/GITHUB/OnlyRag/skills/windows-packaging-signing/SKILL.md): NSIS packaging, signtool.exe signing, prerequisite testing, installer release lifecycle.
- [`skills/code-maintenance-automation`](file:///d:/GITHUB/OnlyRag/skills/code-maintenance-automation/SKILL.md): Automated code formatting, static linting, and testing workflows.

### Setup & Prerequisites Bootstrap
```powershell
pwsh .\scripts\Bootstrap-Prerequisites.ps1
```

### Local Build & Start (Production Web Assets)
```powershell
pwsh .\scripts\Build-Web.ps1
dotnet run --project .\src\OnlyRag.App\OnlyRag.App.csproj --configuration Debug
```

OCR runtime recovery is session-safe: querying the OCR dependency status automatically starts at
most one background repair for an incomplete or corrupt private environment. Progress, timeout,
cancellation, and failure remain observable through the dependency status endpoint; after a failed
attempt the UI exposes explicit manual repair without retry loops.

### Development Mode (Vite Dev Server + WPF Shell)
In Terminal 1:
```powershell
Set-Location .\src\OnlyRag.Web
npm run dev
```
In Terminal 2:
```powershell
$env:ONLYRAG_WEB_DEV_SERVER = "http://127.0.0.1:5173"
dotnet run --project .\src\OnlyRag.App\OnlyRag.App.csproj --configuration Debug
```

### Canonical Readiness Gate
```powershell
# Fast gate check (recommended for rapid development & AI agent verification)
pwsh .\scripts\Invoke-Gate.ps1 -Fast

# Full release verification gate with tests
pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release
```
Runs preflight checks, frontend typecheck/lint/format, OCR manifest checks, web build, and .NET build. Supports `-Fast` (or `-SkipTests`) to streamline local verification, and `-IncludeAudits` for package vulnerability audits.

### Packaging & Release Pipeline
```powershell
# 1. Package Readiness Gate with Installer Compilation
pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release -IncludeInstaller

# 2. Sign Release Installer
pwsh .\scripts\Sign-Release.ps1 -CertificateThumbprint <thumbprint>

# 3. Test Installer Lifecycle
pwsh .\scripts\Test-InstallerRelease.ps1 -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe -RequireSigned -RunInstallLifecycle
```

### Workspace Cleanup
```powershell
pwsh .\scripts\Clean.ps1
```

## 5. Development Guidelines & Rules

- Load the narrowest applicable skill before editing a subsystem. Use the repository file reader (`view`) on `skills\<name>\SKILL.md`; do not invent a skill name or rely on a stale copy.
- Use `skills\onlyrag\SKILL.md` for cross-cutting work, `dotnet-wpf-minimal-api` for C#/WPF/API, `react-vite-frontend` for the Web UI, `rag-vector-retrieval` for ingestion/search, `autonomous-agent-engine` for agent runs, `onnx-directml-image-gen` for image generation, `windows-packaging-signing` for release work, and `code-maintenance-automation` for quality scripts.
- Preserve the architecture and user data by default. Remove obsolete code only when it is in scope and the replacement is verified.
- Use PowerShell paths from the repository root, keep tests/builds/lint serial, and stop on the first regression.
- Never store certificates or credentials in the repository. Use the Windows credential/vault integrations and documented local environment overrides.
- Do not edit generated outputs manually; update their source or generator and rerun it.
- Do not declare work complete without the relevant targeted check and, for cross-cutting changes, `Invoke-Gate.ps1 -Fast` or the Release gate.
