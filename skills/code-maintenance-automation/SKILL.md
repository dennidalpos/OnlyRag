---
name: code-maintenance-automation
description: Official-source guidance for the repository formatting, linting, build and serial test utilities.
---

# Code Maintenance Automation Skill

Use this skill for changes to `scripts/`, project quality configuration, or repository-wide verification. The utilities are PowerShell entrypoints executed from the repository root.

## Official sources

- .NET format: https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-format
- .NET analyzers and code style: https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview
- PowerShell scripting: https://learn.microsoft.com/en-us/powershell/scripting/overview
- npm scripts: https://docs.npmjs.com/cli/v11/using-npm/scripts
- TypeScript compiler: https://www.typescriptlang.org/docs/handbook/compiler-options.html
- ESLint: https://eslint.org/docs/latest/
- Prettier: https://prettier.io/docs/en/
- Vitest: https://vitest.dev/guide/
- xUnit command line: https://xunit.net/docs/getting-started/netcore/cmdline

Only vendor or project-maintainer primary documentation may be added to this list. Do not use blogs, copied snippets or unverified benchmark claims as operational authority.

## Utility contracts

- `scripts\Format-Code.ps1`: runs `dotnet format` for the solution and the configured frontend Prettier/text checks. Use `-CheckOnly` in CI or before handoff.
- `scripts\Lint-Code.ps1`: runs TypeScript typecheck, ESLint and the .NET analyzer build. Use `-Configuration Release` to enforce warnings as errors.
- `scripts\Test-Code.ps1`: runs Vitest and the .NET suite with serial execution; slow integration and Playwright runs are opt-in.
- `scripts\test-agent.ps1`: compact PASS/FAIL runner for agent work; `-Full` enables the complete manual suite.
- `scripts\Invoke-Gate.ps1`: canonical readiness gate. `-Fast` skips tests and audits; the Release gate runs the configured verification stages.

The frontend package scripts are the source of truth for their individual commands. Do not duplicate command lists in new scripts; call the existing scripts and propagate non-zero exit codes.

## Required execution order

Run checks one at a time. Stop on the first failure, correct it, and rerun that check before continuing:

```powershell
pwsh .\scripts\Format-Code.ps1 -CheckOnly
pwsh .\scripts\Lint-Code.ps1 -Configuration Release
pwsh .\scripts\test-agent.ps1
pwsh .\scripts\Invoke-Gate.ps1 -Fast
```

Use the full Release gate before packaging or release handoff. Do not start test, build or lint processes concurrently.

## Script implementation rules

- Use `#requires -Version` consistently with the existing script (`5.1` for gate/test scripts, `7.0` for formatting/linting).
- Resolve paths from `$PSScriptRoot`; never depend on the caller's current directory after the root entrypoint has started.
- Set `$ErrorActionPreference = "Stop"` and propagate native command exit codes. Do not catch and ignore failures.
- Keep output compact in agent mode and provide verbose output only through an explicit switch.
- Do not mutate source files during check-only or gate operations.
