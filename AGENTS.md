# AGENTS.md — Repository Instructions

Repository-specific instructions for Codex.

Primary local environment: **Windows + PowerShell**.

Use repository conventions when they are clear. Keep changes small, focused, verifiable, and limited to the requested task.

---

## 1. Priorities

1. Preserve user work.
2. Make the smallest correct repository change.
3. Keep the repository clean and navigable.
4. Follow existing repository conventions.
5. Verify with available repository checks.
6. Keep `PROJECT_STATUS.json` as a todo-only file when present or requested.
7. Report changes, checks, and remaining uncertainty.

Do not change unrelated files. Do not introduce dependencies, public API changes, config/deployment changes, broad refactors, migrations, or destructive operations unless the task clearly requires them.

Do not claim a check passed unless it was actually run and passed.

---

## 2. Fresh project policy

When creating or initializing a new project, treat it as fresh/greenfield unless the user explicitly says it must integrate with legacy systems.

Do not add legacy compatibility layers, migration scaffolding, deprecated patterns, transitional folder names, backward-compatibility shims, historical cleanup work, or assumptions about previous production users/data/APIs.

Prefer current conventions, clean architecture, minimal structure, and only the files needed for the requested scope.

---

## 3. Windows-first rules

Use PowerShell-compatible commands unless repository evidence requires another shell, container, CI image, or deployment target.

Assume local development is Windows. Avoid local assumptions about Bash, WSL, GNU-only flags, `/tmp`, `/home`, `chmod`, `sed -i`, `rm -rf`, or Unix-only path separators.

Use repository-relative paths, quote paths that may contain spaces, avoid hardcoded absolute paths, and prefer cross-platform path APIs in code.

---

## 4. Workflow

For implementation tasks:

1. Inspect only files needed for the requested change, including applicable local instructions and nearby patterns.
2. Check the working tree before editing:

   ```powershell
   git status --short
   ```

3. Do not overwrite unrelated uncommitted user changes.
4. Implement the smallest production-quality change.
5. Add or update tests when behavior changes and a test framework exists.
6. Update docs only when setup, commands, behavior, public API, deployment, scripts, or structure change.
7. Run the most relevant available checks.
8. Perform a cleanliness check before the final response.

For read-only tasks, do not modify files. Report findings, affected areas, recommended fixes, and review limits.

---

## 5. Cleanliness check

Before the final response for implementation tasks, verify that:

- unrelated files were not changed;
- new files are in the correct responsibility folder;
- generated, temporary, debug, build, report, or log files were not left in source folders;
- stale references were not left after renames or splits;
- duplicate scripts, configs, assets, or docs were not introduced;
- `.gitignore` covers local/generated outputs when appropriate.

Minimum commands when available:

```powershell
git status --short
git ls-files
```

Then run relevant project-native checks: format, lint, typecheck, tests, build, or security checks. State why any relevant check was not run.

---

## 6. `PROJECT_STATUS.json`

`PROJECT_STATUS.json` is optional unless already present or explicitly requested.

When present, `PROJECT_STATUS.json` serves as the authoritative backlog for pending implementation tasks:
1. **Register Remaining Scope**: Any feature, optimization, or implementation task requested by the user that is not yet fully completed MUST be registered as an actionable task in `PROJECT_STATUS.json`.
2. **Persistent Task Execution**: At the start of every implementation task and in subsequent prompts, the agent MUST inspect `PROJECT_STATUS.json` using `view_file` and prioritize implementing any remaining todo items.
3. **Iterative Cleanup**: Remove completed, obsolete, or invalidated items immediately as work is verified, leaving `PROJECT_STATUS.json` with `"todos": []` when all tasks are complete.

Allowed schema:

```json
{
  "todos": [
    "Short actionable task"
  ]
}
```

Rules:

- Remove completed, obsolete, duplicated, invalidated, or historical items.
- Do not store completed work, prompt/chat history, secrets, credentials, personal data, check results, decisions, assumptions, risks, blockers, timestamps, changelog entries, or status notes.
- Prefer fewer accurate todos over many stale todos.
- Do not update it for read-only tasks unless requested.

---

## 7. Structure and files

Use the existing structure when coherent. For new or unclear areas, start minimal and add folders only when real responsibilities exist.

Keep each source file focused on one responsibility. Split files only when responsibilities are mixed or maintenance is clearly worse without a split. Do not perform large unrelated splits.

Prefer explicit names. Avoid vague folders like `misc`, `stuff`, `old`, `new`, `final`, `temp2`, `backup`; avoid vague files like `utils.*`, `helpers.*`, `common.*`, `manager.*`, or unqualified `service.*` unless already established by the repository.

`index.*` files should contain exports, framework-required entrypoints, or very small composition code; not substantial implementation logic.

---

## 8. Scripts

Use repository-native scripts first.

When adding Windows-first local scripts, prefer PowerShell wrappers under `scripts/`.

Scripts must run from the repository root, validate required tools, fail with non-zero exit codes on errors, and avoid duplicating an existing script. Keep public scripts thin; put shared script logic in `scripts/support/` when needed.

Update `scripts/README.md` when public scripts are added, removed, or renamed.

---

## 9. Verification

When behavior changes, add or update tests using the existing framework.

If no test framework exists, do not install one automatically unless required. Provide a practical manual verification path instead.

Run relevant available checks and report exact commands and results. Do not stop at the first failed check if useful static review can continue.

---

## 10. Security and dependencies

Never create, print, commit, or expose secrets.

Use `.env.example` for required environment variables. Do not put credentials, private keys, tokens, passwords, or personal data into docs, logs, examples, or status files.

Validate external input, escape output where required, use parameterized database queries, and avoid logging sensitive data.

Before adding a dependency, check whether the repository already has a suitable dependency. Prefer the standard library or existing utilities. Respect the existing package manager and lockfile. Do not add dependencies only for convenience.

---

## 11. Git hygiene

Inspect the working tree before editing. Do not overwrite user changes.

Avoid destructive Git operations: `git reset --hard`, `git clean -fd`, force pushes, branch deletion, and history rewriting.

Only stage or commit when explicitly requested.

---

## 12. Final response

For implementation tasks, include:

- what changed;
- files changed;
- checks run and results;
- cleanliness result;
- `PROJECT_STATUS.json` todo update, if applicable;
- remaining risks, blockers, or next steps.

For review-only tasks, include findings by severity, affected files/areas, suggested fixes, assumptions, and review limits.

Be factual and concise. Do not claim production readiness unless relevant checks passed or limitations are clearly stated.

---

## 13. Project Skills (`skills/`)

The repository maintains project-specific skills under the `skills/` directory in the repository root for portability across different development environments and PCs. Each skill provides technical guidelines, official documentation references, operational commands, and architectural standards for a specific domain area:

- [`skills/onlyrag`](skills/onlyrag/SKILL.md): Primary project architecture, scripts inventory, workflow commands, local app data paths, and gate verification.
- [`skills/dotnet-wpf-minimal-api`](skills/dotnet-wpf-minimal-api/SKILL.md): C# .NET 10 WPF host shell, Minimal API backend, WebView2 interop bridge, SQLite storage, and xUnit testing.
- [`skills/react-vite-frontend`](skills/react-vite-frontend/SKILL.md): React 19, Vite 8, TypeScript 5.9, Tailwind CSS, Vitest 4, ESLint 10, Prettier 3.9, and Playwright 1.62 UI tests.
- [`skills/rag-vector-retrieval`](skills/rag-vector-retrieval/SKILL.md): Local RAG 2.0 architecture, Dual-Tier Parent-Child chunking, SQLite FTS5 keyword indexing, Qdrant vector database, Ollama embeddings/chat, ONNX Cross-Encoder re-ranking, CRAG confidence evaluation, and retrieval metrics.
- [`skills/onnx-directml-image-gen`](skills/onnx-directml-image-gen/SKILL.md): ONNX Runtime DirectML GPU acceleration and CPU fallback on Windows, SDXL/LCM models, Hugging Face metadata, SHA256 verification, and canvas editing.
- [`skills/windows-packaging-signing`](skills/windows-packaging-signing/SKILL.md): NSIS packaging, `signtool.exe` signing, prerequisite checks, and release lifecycle testing.
- [`skills/code-maintenance-automation`](skills/code-maintenance-automation/SKILL.md): Automated code formatting, static linting, and testing workflows across .NET and React.

### Agent Directives for Skills & Maintenance:

1. **Load Skills at Task Start**: When working on tasks in a specific domain area (e.g., frontend React, .NET backend, vector search, image generation, packaging, maintenance), inspect and load the relevant `skills/<skill-name>/SKILL.md` instruction file using `view_file`.
2. **Maintain and Update Skills**: Continuously update and maintain the skill files under `skills/` whenever repository commands, architecture, dependencies, scripts, or official standards change.
3. **Official Sources Only**: All skill instructions and reference links must rely strictly on official documentation sources (Microsoft Learn, React, Vite, SQLite, Qdrant, Ollama, ONNX Runtime, NSIS, ESLint, Prettier, etc.).
4. **Cross-Machine Synchronization**: Ensure all skills under `skills/` remain checked into the root repository so they are immediately available on any PC or development environment.
5. **Code Quality & Maintenance Automation**: Use `pwsh .\scripts\Format-Code.ps1` for code formatting, `pwsh .\scripts\Lint-Code.ps1` for static linting/typechecking, `pwsh .\scripts\Test-Code.ps1` for automated unit tests, and `pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release` for canonical gate verification.
6. **Continuous Documentation & Skill Synchronization**: Always update repository documentation (`docs/`) and skill files (`skills/`) whenever code structures, public APIs, dependencies, configuration, or operational commands are added, modified, or deprecated.
7. **Zero Technical Debt Policy**: Promptly remove legacy scaffolding, obsolete planning files, unused/dead code, and temporary build or test artifacts to ensure a pristine, maintainable repository state.



