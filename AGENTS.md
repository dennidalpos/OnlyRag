# AGENTS.md — Instructions Summary

## 1. Scope & Principles
* **Priority Order:** User request > Specific `AGENTS.md` > Main `AGENTS.md`. In case of conflict, choose the safest, least invasive option and report it.
* **Principles:**
  * Greenfield mindset: build strictly for current requirements. No speculative abstractions, shims, or temporary code.
  * Keep the repo clean, navigable, and domain-focused.
  * Entrypoints must remain thin; put logic in domain modules.

## 2. Environment (Windows & PowerShell)
* Primary dev environment: **Windows / PowerShell / UTF-8**.
* Avoid Unix-only commands/paths (`/tmp`, `chmod`, `rm -rf`, `sed -i`).
* Use cross-platform APIs (`pathlib`), quoted paths, and repository-relative paths.

## 3. Assessment & Working Tree
* Read relevant `AGENTS.md` files and inspect affected areas.
* Check git status (`git status --short`) before starting. Preserve pre-existing, unrelated work.

## 4. Implementation Rules
* **Architecture:** Preserve structure unless refactoring is required. Remove obsolete code completely—do not leave compatibility layers.
* **Dependencies:** Prefer standard library or existing dependencies. Respect lockfiles; do not upgrade unrelated packages or change package managers without cause.
* **Security:** Never expose, print, or commit secrets/tokens. Use `.env.example` and environment variables.

## 5. Repository Structure & Scripts
* **Naming:** Use clear, domain-specific names. Avoid generic/catch-all folders or files (`misc/`, `temp/`, `utils.*`, `common.*`).
* **Scripts:** Place scripts in standard locations (default: `scripts/`), executable from repo root, returning non-zero exit codes on failure.

## 6. Testing & Verification
* Update/add tests for any behavior changes.
* **Agent Fast Mode:** Automated tests run by AI must run in fast/summarized mode (concise output: PASS/FAIL + minimal stack trace). PowerShell test scripts must support both fast mode (default for agents) and full/manual mode (for debugging).
* **Honesty:** Never claim a test, linter, or build was run if it was not.

## 7. Documentation
* Keep setup, usage, API, and architectural documentation synchronized with all code changes.

## 8. PROJECT_STATUS.json
* Strict active todo list (`{"todos": ["Task"]}`).
* Add active tasks only; immediately remove completed, obsolete, or duplicate tasks. No changelogs, blockers, or notes.

## 9. Final Verification
* Run tests (fast mode), linters, formatters, type-checkers, and builds.
* Review diff and `git status --short`. Clean up temporary files, logs, and stale references. Ensure `.gitignore` is updated.
* **Forbidden Git Commands:** `git reset --hard`, `git clean`, force-push, or history rewriting.

## 10. Final Response Format
For implementation tasks, structure the response concisely into:
1. **What changed:** Functional updates, structural improvements, removed code.
2. **Files modified/deleted:** List of created, edited, or deleted files.
3. **Checks run & results:** Real results for tests/lint/builds (or explicit reason if skipped).
4. **Cleanliness status:** Confirmation that no temp/secret files remain.
5. **Remaining limitations / risks:** Concrete risks only.
6. **Next steps:** Immediate necessary follow-ups only.