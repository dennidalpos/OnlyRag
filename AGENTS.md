# AGENTS.md — Project Instructions & Global Rules

## 1. Scope, Priority & Greenfield Mindset

* **Priority:** User request > Project `AGENTS.md` > Global defaults.
* **Zero Speculation / Directives First:** Follow user instructions strictly. Build ONLY what is specified. NEVER invent unasked features, speculative abstractions, extra architectural layers, temporary code, or unrequested dependencies.
* **Architecture:** Keep entrypoints thin; place logic in domain modules. Preserve existing architecture unless refactoring is explicitly requested.
* **Clean Code & Legacy Maintenance:** Develop clean, scalable code following modern best practices. Never leave dead, unused, or legacy code.
* **Uncertainty & Ambiguity:** If in doubt, stop immediately and ask the user before proceeding.

## 2. Environment (Windows & PowerShell)

* **Dev Environment:** **Windows / PowerShell / UTF-8**.
* **Syntax & Commands:** Avoid Unix-only syntax/commands (`/tmp`, `chmod`, `rm -rf`, `sed -i`).
* **Paths & APIs:** Use cross-platform APIs (`pathlib`), quoted paths, and repository-relative paths.
* **Git Status Check:** Check working tree (`git status --short`) before starting. Preserve pre-existing, unrelated work.

## 3. Strict Serial Execution & Testing

* **Sequential Workflows Only:** Execute ALL tests, builds, linters, and compilations strictly one by one in serial. Parallel/concurrent jobs are explicitly forbidden to prevent test flakiness, race conditions, and context/token waste.
* **Agent Fast Mode:** Automated tests run by AI must run in fast/summarized mode (concise output: `PASS/FAIL` + minimal stack trace on failure). PowerShell test scripts must support both fast mode (agent default) and full/manual mode (debugging).
* **Honesty:** Update/add tests for code changes. Run sequential checks before reporting completion. Never claim checks or tests were run if they were skipped.

## 4. Dependencies, Security & Knowledge Sources

* **Dependencies:** Prefer standard library or existing dependencies. Respect lockfiles; do not upgrade packages without explicit approval.
* **Security:** Never expose, print, or commit secrets/tokens. Use `.env.example` and environment variables.
* **Forbidden Git Commands:** NEVER run `git reset --hard`, `git clean`, force-push, or history rewriting.
* **Information Sources:** Use official docs first. If unresolved, search community sources progressively down the hierarchy.

## 5. Repository Structure, Skills & Documentation

* **Scripts:** Place scripts in standard locations (default: `scripts/`), returning non-zero exit codes on failure.
* **Active Tasks:** Maintain a strict active todo list in `PROJECT_STATUS.json`: `{"todos": ["Task"]}`. Add active tasks only; immediately remove completed or obsolete items. No changelogs, blockers, or notes.
* **Project Skills:** Generate, maintain, and update project skills/capabilities as the repository grows.
* **Documentation Maintenance:** Maintain minimal, concise project documentation. Update documentation immediately with code changes to prevent drift.

## 6. Final Agent Response Format

Minimize response size to conserve tokens. Structure final responses strictly into:

1. **What changed:** Functional updates and structural improvements.
2. **Files modified/deleted:** List of created, edited, or deleted files.
3. **Checks run & results:** Real results for sequential tests/builds (or explicit reason if skipped).
4. **Cleanliness status:** Confirmation that no temp/secret files remain.
5. **Remaining limitations / risks:** Concrete risks only.
6. **Next steps:** Immediate necessary follow-ups only.