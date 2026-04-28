# Scripts

Run repository scripts from the repository root in PowerShell 7.

## Canonical Scripts

| Script | Scope | Inputs | Outputs | Dependencies | Referenced by |
|---|---|---|---|---|---|
| `Bootstrap-Prerequisites.ps1` | Setup and dependency install | `-SkipNode`, `-SkipOcr`, `-SkipOllamaCheck`, `-NonInteractive`, `-LibreOfficePath` | `%LOCALAPPDATA%\OnlyRag` directories, `dotnet restore`, `npm ci`, optional OCR venv | Windows, PowerShell 7, .NET 10 SDK, WebView2, Node/npm, optional Python/Ollama/LibreOffice | `README.md`, `docs\OPERATIONS.md`, OCR docs |
| `Build-Web.ps1` | Web build | none | `src\OnlyRag.Web\dist` | npm, `src\OnlyRag.Web\package-lock.json`, `scripts\internal\BuildSupport.ps1` | `README.md`, `docs\OPERATIONS.md`, app static UI flow |
| `Build-App.ps1` | .NET restore and build | `-Configuration Debug|Release` | .NET build outputs under project `bin` folders | .NET 10 SDK, `OnlyRag.sln` | `README.md`, `docs\OPERATIONS.md` |
| `Test-All.ps1` | Repository verification | `-Configuration Debug|Release` | dotnet test result, web typecheck result | .NET 10 SDK, npm dependencies | CI, `docs\OPERATIONS.md` |
| `Build-Installer.ps1` | Package installer | `-Configuration`, `-RuntimeIdentifier`, `-Version`, `-OutputRoot`, `-InnoSetupCompiler`, optional signing inputs | publish payload and installer under `artifacts` | .NET 10 SDK, npm, Inno Setup 6, optional signtool/certificate, `scripts\internal\BuildSupport.ps1` | packaging docs, signing script |
| `Sign-Release.ps1` | Signed release candidate build | certificate path or thumbprint, optional signing/tool paths | signed installer and release evidence | `Build-Installer.ps1`, Windows certificate store, signtool, `Test-InstallerRelease.ps1` | `docs\SIGNING.md`, release backlog |
| `Test-InstallerRelease.ps1` | Installer evidence | `-InstallerPath`, optional upgrade/rollback paths, `-RequireSigned`, `-RunInstallLifecycle` | JSON evidence under `artifacts\release-verification` | installer artifact, optional clean verification machine, `scripts\internal\BuildSupport.ps1` | packaging docs, signing docs |

## Runtime Bridge

`scripts\ocr` is a product runtime bridge copied into build and publish output by
`src\OnlyRag.Infrastructure\OnlyRag.Infrastructure.csproj`. It is not a developer command folder.

## Internal Helpers

`scripts\internal\BuildSupport.ps1` contains shared PowerShell functions used by build,
packaging, signing, and installer verification scripts. Do not call it directly.

## Agent Scripts

`scripts\agents\Gate-Build.ps1` is an agent/local gate that kills local OnlyRag-related processes,
wipes selected `%LOCALAPPDATA%\OnlyRag` data subdirectories, cleans outputs, builds, tests, publishes,
and optionally compiles the installer. It is intentionally outside the canonical top-level script set.
