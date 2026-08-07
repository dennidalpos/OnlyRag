# AGENTS.md — Project Instructions & Global Rules

## 1. Scope, Priority & Greenfield Mindset
* **Priority:** User request > Project `AGENTS.md` > Global defaults.
* **Zero Speculation / Directives First:** Follow user instructions strictly. Build ONLY what is specified. NEVER invent unasked features, speculative abstractions, extra architectural layers, temporary code, or unrequested dependencies.
* **Architecture:** Keep entrypoints thin; place logic in domain modules. Preserve existing architecture unless refactoring is explicitly requested.

## 2. Environment (Windows & PowerShell)
* Dev environment: **Windows / PowerShell / UTF-8**.
* Avoid Unix-only syntax/commands (`/tmp`, `chmod`, `rm -rf`, `sed -i`).
* Use cross-platform APIs (`pathlib`), quoted paths, and repository-relative paths.
* Check working tree (`git status --short`) before starting. Preserve pre-existing, unrelated work.

## 3. Strict Serial Execution & Testing
* **Sequential Workflows Only:** Execute ALL tests, builds, linters, and compilations strictly one by one in serial. Parallel/concurrent jobs are explicitly forbidden to prevent test flakiness, race conditions, and context/token waste.
* **Agent Fast Mode:** Automated tests run by AI must run in fast/summarized mode (concise output: `PASS/FAIL` + minimal stack trace on failure). PowerShell test scripts must support both fast mode (agent default) and full/manual mode (debugging).
  * Use `.\scripts\test-agent.ps1` to run the agent-optimized fast test suite.
  * Use `.\scripts\Test-Code.ps1` for local test runs (excludes the slow backend workflow by default).
  * Use `.\scripts\Invoke-Gate.ps1` to run the full pre-commit pipeline validation.
* **Honesty:** Update/add tests for code changes. Run sequential checks before reporting completion. Never claim checks or tests were run if they were skipped.

## 4. Dependencies, Security & Git Safety
* **Dependencies:** Prefer standard library or existing dependencies. Respect lockfiles; do not upgrade packages without explicit approval.
* **Security:** Never expose, print, or commit secrets/tokens. Use `.env.example` and environment variables.
* **Forbidden Git Commands:** NEVER run `git reset --hard`, `git clean`, force-push, or history rewriting.

## 5. Repository Structure & PROJECT_STATUS.json
* Place scripts in standard locations (default: `scripts/`), returning non-zero exit codes on failure.
* Strict active todo list in `PROJECT_STATUS.json`: `{"todos": ["Task"]}`. Add active tasks only; immediately remove completed or obsolete items. No changelogs, blockers, or notes.

## 6. Final Agent Response Format
Minimize response size to conserve tokens. Structure final responses strictly into:
1. **What changed:** Functional updates and structural improvements.
2. **Files modified/deleted:** List of created, edited, or deleted files.
3. **Checks run & results:** Real results for sequential tests/builds (or explicit reason if skipped).
4. **Cleanliness status:** Confirmation that no temp/secret files remain.
5. **Remaining limitations / risks:** Concrete risks only.
6. **Next steps:** Immediate necessary follow-ups only.