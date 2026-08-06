# AGENTS.md — Global Defaults

## Priority

User request > repository/nested `AGENTS.md` > this file.

If instructions conflict, choose the least invasive safe option and mention it.

## Environment

Default: Windows + PowerShell.

Prefer PowerShell-compatible commands, UTF-8, quoted paths, and cross-platform path APIs (`pathlib`, `path`). Do not assume Bash/WSL/GNU tools unless required by the project.

## General

Assume new projects are greenfield unless the repository indicates otherwise.

For greenfield:
- No compatibility layers, migrations, shims, or legacy patterns unless requested.
- Prefer current framework conventions and simple architectures.

For existing projects:
- Respect existing architecture.
- Make the smallest safe, consistent change.

Avoid unnecessary rewrites, dependency changes, API changes, or refactors.

Only ask for clarification if the change risks data loss, security issues, irreversible actions, or major architectural decisions.

## Editing

Read only the files needed (including relevant `AGENTS.md`).

Preserve user changes.

Never use destructive Git commands (`reset --hard`, `clean`, force-push, history rewrite).

## Quality

When behavior changes, update/add tests if the project has them.

Run relevant checks when practical (format, lint, typecheck, tests, build).

Never claim checks were run if they were not.

## Documentation & Synchronization

Documentation in `docs/` and root `README.md` MUST be kept strictly synchronized with the codebase at all times.

- Whenever code features, schemas, API endpoints, runtime models, script flows, or architectural components are modified or added, update the corresponding documentation files in `docs/` and root `README.md` immediately within the same task.
- Remove or update obsolete, redundant, or misleading documentation references to prevent drift.
- Ensure all technical diagrams, schema version references, and path listings accurately match the codebase implementation.

## Skills & Discovery

Future AI agents working on this repository MUST inspect, load, and follow the specialized development skills located in `skills/` and `skill/` whenever performing relevant tasks:

- [`skills/onlyrag`](file:///d:/GITHUB/OnlyRag/skills/onlyrag/SKILL.md) / [`skill/onlyrag`](file:///d:/GITHUB/OnlyRag/skill/onlyrag/SKILL.md): Primary architecture, workflows, local data paths, and gate check.
- [`skills/dotnet-wpf-minimal-api`](file:///d:/GITHUB/OnlyRag/skills/dotnet-wpf-minimal-api/SKILL.md) / [`skill/dotnet-wpf-minimal-api`](file:///d:/GITHUB/OnlyRag/skill/dotnet-wpf-minimal-api/SKILL.md): C# .NET 10 WPF host shell, Minimal API backend, WebView2 interop, SQLite.
- [`skills/react-vite-frontend`](file:///d:/GITHUB/OnlyRag/skills/react-vite-frontend/SKILL.md) / [`skill/react-vite-frontend`](file:///d:/GITHUB/OnlyRag/skill/react-vite-frontend/SKILL.md): React 19, Vite, TypeScript, Tailwind CSS, Lucide icons, Vitest, Playwright.
- [`skills/rag-vector-retrieval`](file:///d:/GITHUB/OnlyRag/skills/rag-vector-retrieval/SKILL.md) / [`skill/rag-vector-retrieval`](file:///d:/GITHUB/OnlyRag/skill/rag-vector-retrieval/SKILL.md): Dual-Tier chunking, SQLite FTS5, Qdrant, Heuristic Re-ranking, CRAG evaluation.
- [`skills/autonomous-agent-engine`](file:///d:/GITHUB/OnlyRag/skills/autonomous-agent-engine/SKILL.md) / [`skill/autonomous-agent-engine`](file:///d:/GITHUB/OnlyRag/skill/autonomous-agent-engine/SKILL.md): MCTS Tree-of-Thought, Plan-Act-Observe-Verify state machine, Episodic memory, Subagent DAG.
- [`skills/onnx-directml-image-gen`](file:///d:/GITHUB/OnlyRag/skills/onnx-directml-image-gen/SKILL.md) / [`skill/onnx-directml-image-gen`](file:///d:/GITHUB/OnlyRag/skill/onnx-directml-image-gen/SKILL.md): ONNX DirectML GPU / CPU fallback, SDXL/LCM models, SHA256 integrity, Canvas editor.
- [`skills/windows-packaging-signing`](file:///d:/GITHUB/OnlyRag/skills/windows-packaging-signing/SKILL.md) / [`skill/windows-packaging-signing`](file:///d:/GITHUB/OnlyRag/skill/windows-packaging-signing/SKILL.md): NSIS packaging, signtool.exe signing, prerequisite testing, installer release lifecycle.
- [`skills/code-maintenance-automation`](file:///d:/GITHUB/OnlyRag/skills/code-maintenance-automation/SKILL.md) / [`skill/code-maintenance-automation`](file:///d:/GITHUB/OnlyRag/skill/code-maintenance-automation/SKILL.md): Automated code formatting (`Format-Code.ps1`), static linting (`Lint-Code.ps1`), and testing workflows (`Test-Code.ps1`).

When performing tasks in any of these domain areas, agents MUST load the corresponding `SKILL.md` before writing code or making architectural decisions.

## Security

Never expose secrets.

Use environment variables for configuration.

Prefer existing dependencies or the standard library before adding new ones.

## Final response

For file changes, report:
- files changed
- what changed
- checks run (or why not)
- remaining limitations