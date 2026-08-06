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