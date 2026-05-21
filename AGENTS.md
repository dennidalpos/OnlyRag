# AGENTS.md — Repository Instructions

---

## Local environment

Default local shell: PowerShell on Windows.

Use Windows-compatible commands in examples unless the repository documentation, CI, container, or deployment target requires another environment.

When writing scripts, prefer cross-platform language APIs over shell-specific behavior.

Avoid assuming Bash, WSL, GNU tools, `/tmp`, `/home`, `chmod`, `sed -i`, or `rm -rf` are available.

Respect the actual runtime, CI, container, and deployment target even when they differ from the local Windows environment.

---

## Core operating rules

For every implementation task:

- Keep the repository organized.
- Keep files small and focused.
- Split files when responsibilities diverge or size limits are exceeded.
- Keep script names deterministic and documented.
- Keep generated outputs out of source areas.
- Keep public entrypoints thin.
- Keep reusable logic in reusable modules.
- Verify order and cleanliness before final response.

Do not change unrelated files.

Do not introduce new dependencies, public API changes, database migrations, config format changes, or deployment behavior changes without a clear reason and documentation.

Do not overwrite user changes.

---

## Task workflow

For code-changing tasks:

1. Inspect the relevant repository structure.
2. Read applicable instruction files, `README.md`, setup docs, package/config files, and nearby implementation patterns.
3. Identify the tech stack, package manager, available commands, source roots, generated output paths, and test layout.
4. Check the current working tree before editing.
5. Make the smallest production-quality change that satisfies the request.
6. Place new files in the canonical folder for their responsibility.
7. Split files that exceed the size or responsibility rules in this document.
8. Add or update tests when behavior changes.
9. Run relevant available checks.
10. Run a repository cleanliness review before final response.
11. Update documentation when setup, commands, public APIs, behavior, deployment, repository structure, scripts, or troubleshooting changes.
12. Update `PROJECT_STATUS.json` when this repository uses it or the task explicitly requires project tracking.
13. Summarize the result clearly.

For read-only tasks:

- Do not modify files.
- Provide findings, risks, and recommended next steps.
- Still report structural or cleanliness issues when relevant.

---

## Canonical greenfield repository structure

For projects created from zero, use this structure consistently.

Create the baseline folders and files during project initialization. Keep empty tracked folders only when they represent a stable convention and require a `.gitkeep` or small `README.md`.

```text
project-root/
├─ .codex/
│  └─ notes/                  # optional Codex working notes, not product docs
├─ .github/
│  └─ workflows/              # CI/CD workflows
├─ .vscode/                   # optional editor recommendations only
├─ src/
│  ├─ app/                    # composition root, app bootstrap, routing, DI wiring
│  ├─ features/               # user-facing/domain features
│  │  └─ <feature-name>/
│  │     ├─ components/       # feature UI components, if applicable
│  │     ├─ services/         # feature application services/use cases
│  │     ├─ models/           # feature types/entities/value objects
│  │     ├─ adapters/         # feature-specific external adapters
│  │     ├─ tests/            # feature-local tests when useful
│  │     └─ index.*           # feature public exports only
│  ├─ shared/                 # shared internal code used by multiple features
│  │  ├─ components/
│  │  ├─ constants/
│  │  ├─ errors/
│  │  ├─ logging/
│  │  ├─ utils/
│  │  └─ validation/
│  ├─ services/               # cross-feature application services
│  ├─ infrastructure/         # filesystem, database, network, OS, third-party APIs
│  ├─ config/                 # runtime configuration loading and validation
│  ├─ cli/                    # CLI entrypoints and command wiring
│  ├─ api/                    # API routes, clients, schemas, contracts
│  ├─ assets/                 # source assets imported by the app
│  └─ types/                  # shared types/interfaces only when needed
├─ tests/
│  ├─ unit/
│  ├─ integration/
│  ├─ e2e/
│  └─ fixtures/
├─ scripts/
│  ├─ setup.ps1               # prepare local environment
│  ├─ dev.ps1                 # start development workflow
│  ├─ check.ps1               # full local quality gate
│  ├─ format.ps1              # formatting gate or formatter wrapper
│  ├─ lint.ps1                # lint wrapper
│  ├─ typecheck.ps1           # type/static analysis wrapper
│  ├─ test.ps1                # test wrapper
│  ├─ build.ps1               # build wrapper
│  ├─ package.ps1             # package/release artifact wrapper, if applicable
│  ├─ clean.ps1               # safe cleanup of generated local outputs
│  ├─ README.md               # script inventory and usage
│  └─ support/                # private helper modules used by public scripts
├─ tools/                     # developer tools not part of runtime
├─ config/                    # tool configuration and non-secret project config
├─ env/                       # environment templates and non-secret env docs
├─ data/
│  ├─ samples/                # small committed sample data
│  ├─ fixtures/               # committed test fixtures, if not under tests/
│  ├─ raw/                    # local/raw data, normally ignored
│  └─ processed/              # generated/processed data, normally ignored
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
├─ assets/
│  ├─ images/
│  ├─ icons/
│  └─ fonts/
├─ public/                    # static files served directly
├─ build/                     # generated build output, normally ignored
├─ dist/                      # generated distribution output, normally ignored
├─ logs/                      # local runtime logs, ignored
├─ reports/                   # generated reports, ignored unless intentionally tracked
├─ tmp/                       # temporary files, ignored
├─ .env.example
├─ .gitignore
├─ README.md
├─ AGENTS.md
├─ PROJECT_STATUS.json        # optional operational tracker
└─ CHANGELOG.md
```

### Structure rules

- `src/app/` wires the application together. It must not contain business logic.
- `src/features/<feature-name>/` owns feature-specific behavior.
- `src/shared/` contains reusable code used by at least two features.
- `src/infrastructure/` contains side-effect adapters: filesystem, database, HTTP, OS, subprocesses, external APIs.
- `src/config/` owns configuration loading, defaults, validation, and typed access.
- `src/cli/` contains command parsing and CLI wiring only.
- `src/api/` contains API boundaries, DTOs, clients, schemas, and route wiring.
- `scripts/` contains repeatable operational commands. Public scripts stay thin and call helpers from `scripts/support/`.
- `tools/` contains developer utilities that are not part of normal project operation.
- `docs/` contains human documentation. Do not place operational docs only inside chat notes.
- `reports/`, `logs/`, `tmp/`, `build/`, and `dist/` are generated/local by default and must normally be ignored by Git.
- Do not create generic catch-all folders such as `misc/`, `old/`, `new/`, `final/`, `temp2/`, `stuff/`, or `backup/`.

---

## File responsibility rules

Each source file must have one clear responsibility.

Split a file when it contains more than one of these concerns:

- entrypoint/bootstrap;
- routing or command wiring;
- business logic/use case;
- domain model/types;
- validation/parsing;
- persistence or external adapter;
- UI presentation;
- state management;
- configuration;
- constants;
- error definitions;
- logging;
- test fixtures;
- generated code.

Preferred split patterns:

```text
feature/
├─ index.*                    # public exports only
├─ <feature>.model.*          # types, entities, value objects
├─ <feature>.schema.*         # validation schemas
├─ <feature>.service.*        # use cases/application logic
├─ <feature>.adapter.*        # external integration or persistence
├─ <feature>.controller.*     # API/CLI/UI boundary when needed
├─ <feature>.view.*           # UI-only rendering when applicable
└─ <feature>.test.*           # local tests when convention allows
```

Use existing language/framework naming conventions when they are stronger than this generic pattern.

Avoid files named only:

- `utils.*` when the content is unrelated helpers;
- `helpers.*` when the responsibility is unclear;
- `common.*` without a precise domain;
- `manager.*` unless the managed lifecycle is explicit;
- `service.*` without a feature/domain qualifier;
- `index.*` containing implementation logic.

`index.*` files are allowed only for stable exports, framework-required entrypoints, or very small composition code.

---

## File size policy

Codex must check file size during every implementation task.

Use these thresholds unless the repository defines stricter ones:

| File type | Target | Review required | Split required |
|---|---:|---:|---:|
| Source code | ≤ 250 lines | > 350 lines | > 500 lines |
| UI component/view | ≤ 200 lines | > 300 lines | > 400 lines |
| Application service/use case | ≤ 220 lines | > 320 lines | > 450 lines |
| CLI/API controller | ≤ 200 lines | > 300 lines | > 400 lines |
| Script | ≤ 180 lines | > 250 lines | > 350 lines |
| Test file | ≤ 300 lines | > 450 lines | > 650 lines |
| Markdown doc | ≤ 500 lines | > 800 lines | > 1200 lines |
| JSON/YAML config | keep minimal | > 300 lines | split if tool supports it |

Exemptions:

- lockfiles;
- generated files;
- vendored files;
- migration snapshots;
- intentionally large fixtures;
- framework-required generated manifests;
- compiled outputs, which should usually be ignored.

Rules:

- When a file exceeds “review required”, inspect whether it has multiple responsibilities.
- When a file exceeds “split required”, split it unless there is a documented technical reason not to.
- Record unavoidable oversized files in `PROJECT_STATUS.json` when present.
- Do not split generated, vendored, or lock files.
- Do not perform a massive split in an unrelated task; create a focused follow-up if the split is too large.

---

## Split decision tree

When a file is too large or mixed-responsibility, split in this order:

1. Extract pure types/models to `*.model.*`, `*.types.*`, or `src/types/`.
2. Extract constants and static mappings to `*.constants.*` or `src/shared/constants/`.
3. Extract validation/parsing to `*.schema.*`, `*.validation.*`, or `src/shared/validation/`.
4. Extract external side effects to `*.adapter.*` or `src/infrastructure/`.
5. Extract business logic to `*.service.*` or feature services.
6. Extract UI-only rendering to `*.view.*` or feature components.
7. Extract CLI/API boundary code to `*.controller.*`, `src/cli/`, or `src/api/`.
8. Extract repeated script logic to `scripts/support/`.
9. Extract long documentation sections into `docs/<area>/`.

After splitting:

- Preserve public behavior.
- Update imports/exports.
- Update tests.
- Update documentation if paths changed.
- Run relevant checks.
- Search for stale references to old paths.

---

## Script naming and organization

Public scripts must live in `scripts/` and use stable, lowercase kebab-free base names where practical:

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

Use PowerShell `.ps1` for local operational wrappers when Windows is the primary local environment.

If the project needs cross-platform script logic, put the logic in a language script and keep the PowerShell script as a thin wrapper:

```text
scripts/support/check-project.py
scripts/check.ps1
```

or:

```text
scripts/support/check-project.mjs
scripts/check.ps1
```

Rules:

- Public scripts must be directly runnable from repository root.
- Public scripts must validate required tools before using them.
- Public scripts must fail fast with non-zero exit code.
- Public scripts must print actionable errors.
- Public scripts must not contain large business logic.
- Shared script functions must live in `scripts/support/`.
- Do not create multiple scripts that do the same thing with different names.
- Do not keep obsolete aliases unless required for backward compatibility.
- Document every public script in `scripts/README.md`.

Required `scripts/README.md` columns:

- script;
- path;
- purpose;
- when to use;
- called by;
- prerequisites;
- outputs;
- notes.

Preferred package-manager aliases should call scripts, not duplicate logic:

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

Adapt syntax to the project’s actual package manager and shell constraints.

---

## Repository cleanliness gate

Before final response for every implementation task, check repository order and cleanliness.

At minimum, verify:

- New files are in the correct canonical folder.
- No source file exceeds split thresholds without justification.
- No file mixes unrelated responsibilities.
- No generated outputs are placed in source folders.
- No generated/local folders are accidentally tracked.
- No duplicate script, config, asset, or documentation path was introduced.
- No stale references remain after renames or splits.
- No dead temporary files were left behind.
- README/docs match changed commands, paths, and behavior.
- `.gitignore` covers local generated outputs when appropriate.
- `scripts/README.md` matches current public scripts when scripts changed.

Use repository-native commands when available. If no command exists, perform a manual/static check and report the limit.

Suggested checks when available:

```powershell
git status --short
git ls-files
```

Then use project tools for format, lint, typecheck, tests, and build.

Do not run destructive cleanup commands unless explicitly requested.

---

## Production-readiness standard

Before considering implementation work complete, check that:

- The requested problem is solved completely.
- Code is simple, readable, and modular.
- Files remain within responsibility and size rules.
- Inputs are validated.
- Errors are handled explicitly.
- Secrets are not hardcoded, printed, logged, or committed.
- Logging is useful and avoids sensitive data.
- Tests are added or updated when behavior changes.
- Relevant format, lint, typecheck, test, and build checks are run when available.
- Documentation is updated when usage, setup, architecture, operations, repository structure, scripts, or behavior changes.
- Backward compatibility is preserved unless the user requested a breaking change.
- Migrations, config changes, and deployment implications are documented.

Do not leave TODOs, dead code, commented experiments, debug prints, temporary files, or duplicate folders unless explicitly justified.

---

## `PROJECT_STATUS.json`

`PROJECT_STATUS.json` is optional unless this repository already uses it or the user explicitly requests it.

Use it for meaningful ongoing implementation work, not for quick reviews, one-off diagnostics, or purely read-only tasks.

When present, keep it as a current operational tracker, not a changelog.

Update it when:

- Starting or finishing meaningful implementation work.
- Making an architectural decision.
- Adding, removing, or changing dependencies.
- Changing setup, test, build, deployment, runtime behavior, repository structure, scripts, or file organization.
- Splitting oversized files.
- Discovering a blocker, risk, assumption, or follow-up.
- Completing or invalidating tracked work.

Do not store secrets, credentials, private keys, tokens, passwords, or personal data.

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

Quality gate values:

- `passed`
- `failed`
- `not_available`
- `not_run`
- `unknown`

Keep arrays short and current. Remove completed tasks from `current_tasks`. Use ISO-like timestamps when possible.

---

## Coding standards

Prefer:

- Clear names.
- Small functions.
- Explicit types when the language supports them.
- Dependency injection for external services.
- Pure functions where practical.
- Centralized configuration.
- Consistent formatting with existing tools.
- Existing project conventions over personal preference.
- Domain/feature names in filenames when useful.
- Thin entrypoints and reusable implementation modules.

Avoid:

- Hidden global state.
- Silent failures.
- Broad catch blocks without useful handling.
- Hardcoded paths.
- Hardcoded credentials.
- Over-engineering.
- Copy-pasted logic.
- Unnecessary abstraction.
- Mixing unrelated changes.
- Large multipurpose files.
- Dumping unrelated helpers into generic utility files.

---

## Testing and verification

When changing behavior, add or update tests using the existing project framework.

If no test framework exists:

- Do not install one automatically unless the task clearly requires it.
- Document the missing test setup in `PROJECT_STATUS.json` when the tracker is present.
- Provide a practical manual verification path.

Run relevant checks when available:

- repository cleanliness review;
- formatter;
- linter;
- type checker;
- unit tests;
- integration tests;
- build;
- security checks.

If a check cannot be run, state why.

Do not claim checks passed unless they were actually run and passed.

---

## Security

Never create, print, commit, or expose secrets.

Use `.env.example` for required environment variables.

If a secret appears in files or output:

- Stop using it.
- Do not repeat it.
- Recommend rotation when exposure is likely.
- Record the concern without including the secret value if `PROJECT_STATUS.json` exists.

Validate external inputs.

Escape output where needed.

Use parameterized queries for database access.

Check authentication and authorization boundaries when touching protected flows.

Avoid logging personal data, credentials, tokens, or private keys.

---

## Dependency policy

Before adding a dependency:

- Check whether the project already has a suitable dependency.
- Prefer the standard library or existing utilities.
- Evaluate maintenance, security, size, and production impact.
- Document the reason when the dependency is added.

Do not add dependencies only for convenience.

Respect the existing package manager lockfile.

For Node.js projects:

- `package-lock.json` means npm.
- `pnpm-lock.yaml` means pnpm.
- `yarn.lock` means Yarn.

For Python projects, respect existing environment and dependency files such as:

- `requirements.txt`
- `pyproject.toml`
- `poetry.lock`
- `Pipfile`
- `uv.lock`

---

## Documentation

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

Use `docs/decisions/` only for meaningful architectural decisions. Do not create decision documents for trivial implementation details.

---

## Git hygiene

Before editing, understand the current working tree.

Do not overwrite user changes.

Do not run destructive Git commands unless explicitly requested.

Avoid:

- `git reset --hard`;
- `git clean -fd`;
- force pushes;
- deleting branches;
- rewriting history.

If generated files are created, ensure `.gitignore` is appropriate.

---

## Final response format

For implementation tasks, report:

- What changed.
- Files changed.
- Structure or file split changes.
- Checks run and their result.
- Repository cleanliness result.
- Updates made to `PROJECT_STATUS.json`, if applicable.
- Remaining risks, blockers, or next steps.

For review-only tasks, report:

- Findings by severity.
- Affected files or areas.
- Structure, size, naming, or cleanliness issues.
- Suggested fixes.
- Any assumptions or limits of the review.

Be factual. Do not claim production readiness unless checks passed or limitations are clearly stated.
