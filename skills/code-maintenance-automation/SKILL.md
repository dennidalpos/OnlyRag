---
name: code-maintenance-automation
description: Technical skill for automated code maintenance, linting, formatting, and test verification across OnlyRag C# .NET 10 solution and React 19 / TypeScript frontend.
---

# Code Maintenance & Automation Skill

This skill provides operational guidance for maintaining code quality, formatting, static analysis, and test verification across the OnlyRag repository.

## 1. Official Documentation Sources

- **Microsoft .NET Formatting & Analyzers**: [learn.microsoft.com/dotnet/core/tools/dotnet-format](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-format)
- **ESLint Documentation**: [eslint.org/docs](https://eslint.org/docs/latest/)
- **Prettier Code Formatter**: [prettier.io/docs](https://prettier.io/docs/en/)
- **TypeScript Compiler Options**: [typescriptlang.org/tsconfig](https://www.typescriptlang.org/tsconfig/)
- **PowerShell Scripting Guidelines**: [learn.microsoft.com/powershell/scripting/developer/cmdlet/cmdlet-development-guidelines](https://learn.microsoft.com/en-us/powershell/scripting/developer/cmdlet/cmdlet-development-guidelines)

## 2. Maintenance Commands Summary

All maintenance commands can be run from the repository root using PowerShell 7 (`pwsh`):

### Code Formatting
```powershell
# Format all .NET C# code and Web frontend code (Prettier)
pwsh .\scripts\Format-Code.ps1

# Verify formatting without modifying files
pwsh .\scripts\Format-Code.ps1 -CheckOnly
```

### Static Analysis & Linting
```powershell
# Run ESLint, TypeScript typecheck, and .NET analyzer checks
pwsh .\scripts\Lint-Code.ps1 -Configuration Release
```

### Automated Testing
```powershell
# Run fast Vitest unit tests and .NET unit tests (default compact AI summary)
pwsh .\scripts\Test-Code.ps1 -Fast

# Run full solution integration tests
pwsh .\scripts\Test-Code.ps1 -IncludeIntegration

# Include Playwright end-to-end specifications
pwsh .\scripts\Test-Code.ps1 -IncludeE2e

# Run with full verbose console output (manual developer debugging)
pwsh .\scripts\Test-Code.ps1 -VerboseOutput
```

### Canonical Gate Verification
```powershell
# Fast verification gate (preflight, typecheck, lint, builds, manifest checks - recommended for AI)
pwsh .\scripts\Invoke-Gate.ps1 -Fast

# Full release verification gate with tests (compact AI output)
pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release

# Release verification gate with verbose logs
pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release -VerboseOutput
```

## 3. Operational Best Practices

1. **Pre-Commit / Pre-Handoff Checks**: Always execute `Format-Code.ps1` and `Lint-Code.ps1` before declaring tasks finished.
2. **Zero Debt Principle**: Fix all linting warnings and typecheck errors; do not suppress warnings with ad-hoc comments unless strictly required by platform interop.
3. **Cross-Platform Compatibility**: Keep all scripts compatible with PowerShell 7 (`#requires -Version 7.0`) and relative workspace paths.
