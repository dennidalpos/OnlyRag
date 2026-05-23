# AGENTS.md — Codex Repository Instructions

This file defines how Codex must work in this repository.

Primary local environment: **Windows + PowerShell**.

Use these instructions for implementation, review, refactor, audit, cleanup, and documentation tasks. Prefer repository-specific conventions when they are already clear and stronger than these generic rules.

---

## 1. Operating priorities

Apply these priorities in order:

1. Preserve user work.
2. Make the smallest correct change.
3. Keep the repository clean and navigable.
4. Prefer existing project conventions over new preferences.
5. Verify with available checks.
6. Keep `PROJECT_STATUS.json` current and clean when it exists or is requested.
7. Report exactly what changed, what was checked, and what remains uncertain.

Do not change unrelated files.

Do not introduce new dependencies, public API changes, migrations, config format changes, deployment changes, or broad refactors unless the task clearly requires them.

Do not claim a check passed unless it was actually run and passed.

---

## 2. Windows-first environment rules

Use PowerShell-compatible examples and commands unless the repository explicitly requires another shell, container, CI image, or deployment target.

Assume local development is Windows unless proven otherwise.

Avoid assuming these are available locally:

- Bash;
- WSL;
- GNU-only flags;
- `/tmp`;
- `/home`;
- `chmod`;
- `sed -i`;
- `rm -rf`;
- Unix-only path separators.

Use cross-platform language APIs for scripts whenever practical.

When writing paths:

- Prefer repository-relative paths.
- Avoid hardcoded absolute paths.
- Use path utilities from the project language when writing code.
- Quote paths that may contain spaces.
- Be careful with encoding, CRLF/LF, shell escaping, and glob behavior.

Safe PowerShell examples:

```powershell
git status --short
git ls-files
New-Item -ItemType Directory -Force -Path "reports"
Remove-Item -Recurse -Force "tmp\generated" # only when explicitly safe/requested
```

Do not run destructive cleanup commands unless explicitly requested.

---

## 3. Required workflow for code-changing tasks

For every implementation task:

1. Inspect the repository layout and relevant files.
2. Read applicable local instructions, README/setup docs, package/config files, and nearby implementation patterns.
3. Identify stack, package manager, source roots, generated output paths, test layout, and available checks.
4. Check the working tree before editing:

   ```powershell
   git status --short
   ```

5. Protect user changes. Do not overwrite files with unrelated uncommitted edits.
6. Implement the smallest production-quality change that satisfies the request.
7. Put new files in the correct responsibility folder.
8. Split files only when needed by responsibility or size rules.
9. Add or update tests when behavior changes.
10. Run the most relevant available checks.
11. Perform the repository cleanliness gate.
12. Update documentation when setup, commands, behavior, public API, deployment, scripts, or structure changes.
13. Update `PROJECT_STATUS.json` only when present, requested, or needed by the task.
14. Final response must summarize changes, checks, tracker status, and remaining risks.

For read-only tasks:

- Do not modify files.
- Report findings with severity and affected files.
- Include recommended fixes.
- Mention limits of the review.

---

## 4. Repository cleanliness gate

Before the final response for every implementation task, verify:

- New files are in the correct folder.
- No unrelated files were changed.
- No generated output was placed in source folders.
- No temporary/debug files remain.
- No stale references remain after renames or splits.
- No duplicate scripts, configs, assets, or docs were introduced.
- Source files remain within size and responsibility rules.
- README/docs match changed commands, behavior, paths, and setup.
- `.gitignore` covers local/generated outputs when appropriate.
- `scripts/README.md` matches script changes when scripts are added, removed, or renamed.
- `PROJECT_STATUS.json`, if present, is clean and current.

Minimum commands when available:

```powershell
git status --short
git ls-files
```

Then run project-native checks where relevant, such as format, lint, typecheck, tests, build, or security checks.

If a check cannot be run, state why.

---

## 5. `PROJECT_STATUS.json` tracker policy

`PROJECT_STATUS.json` is optional unless the repository already has it or the user explicitly requests tracking.

When it exists, treat it as a **current operational tracker**, not a changelog, not a diary, and not a history file.

### Mandatory tracker cleanup rule

At every prompt where `PROJECT_STATUS.json` is read or updated:

- Leave the tracker clean.
- Remove completed, obsolete, duplicated, or invalidated tasks.
- Do not append historical notes.
- Do not keep a running log of prompts.
- Do not store chat transcripts.
- Do not accumulate completed work.
- Keep only the current objective, active tasks, next tasks, active blockers, current risks, current decisions, and current verification state.
- Overwrite `last_completed_work` with one concise latest item instead of appending history.
- Keep arrays short and actionable.
- Prefer fewer accurate items over many stale items.

### When to update the tracker

Update `PROJECT_STATUS.json` when:

- Starting or finishing meaningful implementation work.
- Changing architecture, setup, runtime behavior, repository structure, scripts, dependencies, migrations, or deployment behavior.
- Discovering or resolving an active blocker.
- Recording a current risk or assumption that affects future work.
- Splitting oversized files.
- Updating quality gate results.
- The user explicitly asks to update the tracker.

Do not update it for trivial read-only questions unless the user asks.

### Tracker content rules

Never store:

- secrets;
- credentials;
- tokens;
- private keys;
- passwords;
- personal data;
- full prompt history;
- full chat history;
- verbose historical changelog;
- stale completed todo lists.

Use ISO-like timestamps when possible.

Allowed quality gate values:

- `passed`
- `failed`
- `not_available`
- `not_run`
- `unknown`

Preferred schema:

```json
{
  "project_name": "",
  "status": "active",
  "current_phase": "discovery",
  "last_updated": "",
  "production_target": true,
  "summary": "",
  "active_objective": "",
  "assumptions": [],
  "decisions": [],
  "current_tasks": [],
  "next_tasks": [],
  "blockers": [],
  "risks": [],
  "oversized_files": [],
  "structure_notes": [],
  "dependencies": {
    "runtime": [],
    "development": [],
    "external_services": []
  },
  "quality_gates": {
    "format": "unknown",
    "lint": "unknown",
    "typecheck": "unknown",
    "tests": "unknown",
    "build": "unknown",
    "security_review": "unknown",
    "structure_review": "unknown"
  },
  "commands": {
    "install": "",
    "dev": "",
    "check": "",
    "format": "",
    "lint": "",
    "typecheck": "",
    "test": "",
    "build": "",
    "package": "",
    "start": ""
  },
  "environment": {
    "os_target": "Windows",
    "shell": "PowerShell",
    "required_env_vars": [],
    "env_files": []
  },
  "last_completed_work": "",
  "open_questions": [],
  "handoff_notes": ""
}
```

If the existing schema is different, preserve it unless migration is clearly useful. Still keep it clean and non-historical.

---

## 6. Repository structure guidance

Use the existing repository structure when it is coherent.

For new repositories or unclear greenfield areas, prefer this structure:

```text
project-root/
├─ .github/workflows/         # CI/CD
├─ .vscode/                   # optional editor recommendations
├─ src/
│  ├─ app/                    # bootstrap, routing, composition, DI wiring
│  ├─ features/               # domain/user-facing features
│  ├─ shared/                 # reusable internal code used by multiple features
│  ├─ services/               # cross-feature application services
│  ├─ infrastructure/         # filesystem, database, OS, HTTP, external adapters
│  ├─ config/                 # configuration loading and validation
│  ├─ cli/                    # CLI entrypoints and command wiring
│  ├─ api/                    # API routes, clients, schemas, contracts
│  ├─ assets/                 # source assets imported by the app
│  └─ types/                  # shared types/interfaces when needed
├─ tests/
│  ├─ unit/
│  ├─ integration/
│  ├─ e2e/
│  └─ fixtures/
├─ scripts/
│  ├─ setup.ps1
│  ├─ dev.ps1
│  ├─ check.ps1
│  ├─ format.ps1
│  ├─ lint.ps1
│  ├─ typecheck.ps1
│  ├─ test.ps1
│  ├─ build.ps1
│  ├─ clean.ps1
│  ├─ README.md
│  └─ support/
├─ tools/                     # developer tools, not runtime
├─ config/                    # tool config and non-secret project config
├─ env/                       # env templates and non-secret env docs
├─ data/
│  ├─ samples/
│  ├─ fixtures/
│  ├─ raw/                    # usually ignored
│  └─ processed/              # usually ignored
├─ database/
│  ├─ migrations/
│  ├─ seeds/
│  └─ schemas/
├─ docs/
│  ├─ architecture/
│  ├─ decisions/
│  ├─ operations/
│  ├─ setup/
│  ├─ troubleshooting/
│  └─ user-guides/
├─ public/
├─ reports/                   # generated reports, usually ignored
├─ tmp/                       # temporary files, ignored
├─ .env.example
├─ .gitignore
├─ README.md
├─ AGENTS.md
├─ PROJECT_STATUS.json        # optional current tracker
└─ CHANGELOG.md               # release/user-facing history only
```

Structure rules:

- `src/app/` wires the app together and should not contain business logic.
- `src/features/<feature>/` owns feature-specific behavior.
- `src/shared/` is only for code reused by multiple features.
- `src/infrastructure/` contains side-effect adapters.
- `src/config/` owns configuration loading, defaults, validation, and typed access.
- `src/cli/` contains command parsing and CLI wiring only.
- `src/api/` contains API boundaries, DTOs, clients, schemas, and route wiring.
- `scripts/` contains repeatable operational commands.
- Public scripts should stay thin and delegate complex logic to `scripts/support/`.
- `docs/` contains human documentation.
- `reports/`, `logs/`, `tmp/`, `build/`, and `dist/` are generated/local by default and should normally be ignored.

Avoid catch-all folders such as:

- `misc/`;
- `old/`;
- `new/`;
- `final/`;
- `temp2/`;
- `stuff/`;
- `backup/`.

---

## 7. File responsibility rules

Each source file must have one clear responsibility.

Split a file when it mixes multiple concerns:

- bootstrap/entrypoint;
- routing or command wiring;
- business logic/use case;
- domain model/types;
- validation/parsing;
- persistence/external adapter;
- UI presentation;
- state management;
- configuration;
- constants/static mappings;
- error definitions;
- logging;
- test fixtures;
- generated code.

Preferred feature split:

```text
feature/
├─ index.*                    # public exports only
├─ <feature>.model.*          # types, entities, value objects
├─ <feature>.schema.*         # validation schemas
├─ <feature>.service.*        # use cases/application logic
├─ <feature>.adapter.*        # external integration or persistence
├─ <feature>.controller.*     # API/CLI/UI boundary
├─ <feature>.view.*           # UI-only rendering
└─ <feature>.test.*           # local tests when convention allows
```

Avoid vague files unless the project convention is clear and justified:

- `utils.*`;
- `helpers.*`;
- `common.*`;
- `manager.*`;
- unqualified `service.*`;
- `index.*` containing implementation logic.

`index.*` files should only contain stable exports, framework-required entrypoints, or very small composition code.

---

## 8. File size policy

Check file size during implementation tasks.

Use these thresholds unless the repository defines stricter ones:

| File type | Target | Review required | Split required |
|---|---:|---:|---:|
| Source code | ≤ 250 lines | > 350 lines | > 500 lines |
| UI component/view | ≤ 200 lines | > 300 lines | > 400 lines |
| Service/use case | ≤ 220 lines | > 320 lines | > 450 lines |
| CLI/API controller | ≤ 200 lines | > 300 lines | > 400 lines |
| Script | ≤ 180 lines | > 250 lines | > 350 lines |
| Test file | ≤ 300 lines | > 450 lines | > 650 lines |
| Markdown doc | ≤ 500 lines | > 800 lines | > 1200 lines |
| JSON/YAML config | Keep minimal | > 300 lines | Split if supported |

Exemptions:

- lockfiles;
- generated files;
- vendored files;
- migration snapshots;
- intentionally large fixtures;
- framework-generated manifests;
- compiled outputs that should usually be ignored.

Rules:

- If a file exceeds “review required”, check whether responsibilities are mixed.
- If a file exceeds “split required”, split it unless there is a documented technical reason not to.
- Do not split generated, vendored, lock, or migration snapshot files.
- Do not perform large unrelated splits. Record a focused follow-up in the tracker when present.

Split order:

1. Types/models.
2. Constants/static mappings.
3. Validation/parsing.
4. External side effects/adapters.
5. Business logic/services.
6. UI rendering.
7. CLI/API boundary code.
8. Script helpers.
9. Long documentation sections.

After splitting, update imports, exports, tests, docs, and stale references.

---

## 9. Scripts

Use repository-native scripts first.

When adding public local scripts in Windows-first repositories, prefer PowerShell wrappers:

```text
scripts/setup.ps1
scripts/dev.ps1
scripts/check.ps1
scripts/format.ps1
scripts/lint.ps1
scripts/typecheck.ps1
scripts/test.ps1
scripts/build.ps1
scripts/package.ps1
scripts/clean.ps1
```

Rules:

- Scripts must run from the repository root.
- Scripts must validate required tools before use.
- Scripts must fail with non-zero exit codes on errors.
- Scripts must print actionable errors.
- Public scripts must remain thin.
- Shared script logic belongs in `scripts/support/`.
- Do not create duplicate scripts with different names for the same action.
- Document public scripts in `scripts/README.md`.

Required `scripts/README.md` columns:

- script;
- path;
- purpose;
- when to use;
- called by;
- prerequisites;
- outputs;
- notes.

Package-manager aliases should call scripts instead of duplicating logic. Adapt syntax to the actual package manager.

Example:

```json
{
  "scripts": {
    "setup": "powershell -ExecutionPolicy Bypass -File scripts/setup.ps1",
    "check": "powershell -ExecutionPolicy Bypass -File scripts/check.ps1",
    "test": "powershell -ExecutionPolicy Bypass -File scripts/test.ps1",
    "build": "powershell -ExecutionPolicy Bypass -File scripts/build.ps1"
  }
}
```

---

## 10. Testing and verification

When behavior changes, add or update tests using the existing framework.

If no test framework exists:

- Do not install one automatically unless the task requires it.
- Provide a practical manual verification path.
- Record the missing test setup as a current risk in `PROJECT_STATUS.json` when the tracker exists.

Run relevant checks when available:

- format;
- lint;
- typecheck/static analysis;
- unit tests;
- integration tests;
- e2e tests;
- build;
- security/dependency checks;
- repository cleanliness review.

For each check, report:

- command;
- result;
- failure summary, if any;
- reason if not run.

Do not stop at the first failed check if useful static review can continue.

---

## 11. Security rules

Never create, print, commit, or expose secrets.

Use `.env.example` for required environment variables.

If a secret appears in files or output:

1. Stop using or repeating it.
2. Do not include the secret value in the response or tracker.
3. Recommend rotation when exposure is likely.
4. Record only the concern, not the value, when `PROJECT_STATUS.json` exists.

Security expectations:

- Validate external inputs.
- Escape/sanitize output where required.
- Use parameterized database queries.
- Check authentication and authorization boundaries when touching protected flows.
- Avoid logging personal data, credentials, tokens, private keys, or raw sensitive payloads.
- Use least privilege for filesystem, network, and service access.
- Avoid broad catch blocks that hide security-relevant failures.

---

## 12. Dependency policy

Before adding a dependency:

1. Check whether the project already has a suitable dependency.
2. Prefer standard library or existing utilities.
3. Evaluate maintenance, security, size, and production impact.
4. Respect the existing package manager and lockfile.
5. Document why the dependency is needed.

Do not add dependencies only for convenience.

Package manager detection:

- `package-lock.json` means npm.
- `pnpm-lock.yaml` means pnpm.
- `yarn.lock` means Yarn.
- `bun.lock` or `bun.lockb` means Bun.
- `requirements.txt`, `pyproject.toml`, `poetry.lock`, `Pipfile`, or `uv.lock` define Python dependency handling.

Do not mix package managers unless the repository already does so intentionally.

---

## 13. Documentation rules

Update `README.md` or `docs/` when changes affect:

- setup;
- environment variables;
- commands;
- public APIs;
- user-visible behavior;
- deployment;
- operations;
- troubleshooting;
- repository structure;
- script usage;
- file organization;
- split decisions that affect navigation.

Use `docs/decisions/` only for meaningful architectural decisions.

Do not create decision records for trivial implementation details.

Do not use `PROJECT_STATUS.json` as documentation. It is only the current operational tracker.

---

## 14. Git hygiene

Before editing, inspect the current working tree.

Do not overwrite user changes.

Avoid destructive Git operations:

- `git reset --hard`;
- `git clean -fd`;
- force pushes;
- branch deletion;
- history rewriting.

Only stage or commit when explicitly requested.

If generated files are created, ensure `.gitignore` is appropriate.

---

## 15. Final response format

For implementation tasks, include:

- What changed.
- Files changed.
- Structure or file split changes.
- Checks run and results.
- Repository cleanliness result.
- `PROJECT_STATUS.json` update summary, if applicable.
- Remaining risks, blockers, or next steps.

For review-only tasks, include:

- Findings by severity.
- Affected files or areas.
- Structure, size, naming, or cleanliness issues.
- Suggested fixes.
- Assumptions and review limits.

Be factual and concise.

Do not claim production readiness unless the relevant checks passed or the limitations are clearly stated.
