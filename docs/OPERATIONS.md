# Operations And Handoff

OnlyRag operations are Windows-first. Run commands from the repository root with PowerShell 7
(`pwsh`) unless a command explicitly changes directory.

## Prerequisites

Required for development:

- Windows 10 version 1809/build 17763 or newer, or Windows 11.
- PowerShell 7.
- .NET 10 SDK selected by [`global.json`](../global.json).
- Node.js `^20.19.0 || >=22.12.0` with npm.
- Microsoft Edge WebView2 Runtime.
- Microsoft Edge browser for frontend Playwright e2e checks.

Optional by feature:

- Ollama for chat, embeddings, and translation.
- LibreOffice for translation PDF export.
- Python 3.10 through 3.13 for OCR provisioning.
- Integrated image model download from the Images section for image generation.
- Inno Setup 6 for installer builds.
- Windows 10/11 SDK `signtool.exe` and a trusted code-signing certificate for signed release
  candidates.

## Setup

```powershell
pwsh .\scripts\Bootstrap-Prerequisites.ps1
```

Bootstrap verifies host prerequisites, creates `%LOCALAPPDATA%\OnlyRag`, restores .NET packages,
installs frontend dependencies, checks optional Ollama/LibreOffice availability, prepares integrated
image model storage, and prepares OCR when supported Python is available. It does not build, package,
sign, install, or release.

Use these options only when intentionally narrowing setup:

- `-SkipNode`: skip Node/npm checks and frontend dependency install.
- `-SkipOcr`: skip OCR provisioning.
- `-SkipOllamaCheck`: skip Ollama CLI and endpoint checks.
- `-SkipImageGenerationCheck`: skip integrated image model storage checks.
- `-NonInteractive`: avoid prompts and system-level installer actions.
- `-LibreOfficePath <path>`: check a specific `soffice.exe` for PDF export.

## Develop And Start

Static web assets:

```powershell
pwsh .\scripts\Build-Web.ps1
dotnet run --project .\src\OnlyRag.App\OnlyRag.App.csproj --configuration Debug
```

Vite development server:

```powershell
Set-Location .\src\OnlyRag.Web
npm run dev
```

In another PowerShell session:

```powershell
$env:ONLYRAG_WEB_DEV_SERVER = "http://127.0.0.1:5173"
dotnet run --project .\src\OnlyRag.App\OnlyRag.App.csproj --configuration Debug
```

`ONLYRAG_WEB_DEV_SERVER` accepts only loopback `http` or `https` URLs without embedded
credentials.

## Command Map

| task | command |
|---|---|
| Setup | `pwsh .\scripts\Bootstrap-Prerequisites.ps1` |
| Start desktop app | `dotnet run --project .\src\OnlyRag.App\OnlyRag.App.csproj --configuration Debug` |
| Start Vite dev server | `Set-Location .\src\OnlyRag.Web; npm run dev` |
| Check application readiness | `pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release` |
| Check package build readiness | `pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release -IncludeInstaller` |
| Frontend checks | `Set-Location .\src\OnlyRag.Web; npm run typecheck; npm run lint; npm run format:check; npm run test` |
| .NET tests | `dotnet test .\OnlyRag.sln --configuration Release` |
| Build web UI | `pwsh .\scripts\Build-Web.ps1` |
| Build desktop app | `pwsh .\scripts\Build-App.ps1 -Configuration Release` |
| Build unsigned installer | `pwsh .\scripts\Build-Installer.ps1 -Configuration Release` |
| Sign installer | `pwsh .\scripts\Sign-Release.ps1 -CertificateThumbprint <thumbprint>` |
| Verify signed installer lifecycle | `pwsh .\scripts\Test-InstallerRelease.ps1 -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe -RequireSigned -RunInstallLifecycle` |
| Clean generated outputs | `pwsh .\scripts\Clean.ps1` |

## Verification Gates

Application readiness:

```powershell
pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release
```

The gate runs preflight checks, web dependency restore, .NET restore, npm production dependency
audit, NuGet transitive vulnerability audit, frontend typecheck/lint/format/tests, .NET tests,
installer prerequisite self-test, OCR runtime manifest checks, web build, and .NET build.

Diagnostics mode:

```powershell
pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release -ContinueOnError
```

Package build readiness:

```powershell
pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release -IncludeInstaller
```

This requires Inno Setup 6 and compiles the installer. It still does not prove production release
readiness without signing and lifecycle verification.

CI runs `pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release` on `windows-latest`.

## Build And Package

Desktop build:

```powershell
pwsh .\scripts\Build-App.ps1 -Configuration Release
```

`Build-App.ps1` builds the web UI unless `-SkipWebBuild` is supplied, prepares the Qdrant payload
when missing, restores .NET unless `-NoRestore` is supplied, and builds the WPF app project for
the selected runtime.

Installer build:

```powershell
pwsh .\scripts\Build-Installer.ps1 -Configuration Release
```

`Build-Installer.ps1` builds web assets, prepares Qdrant, publishes the WPF app self-contained for
`win-x64`, and compiles the Inno Setup installer. Unsigned output is a packaging artifact only.

## Release Handoff

Before publishing an installer:

1. Run the package gate:

   ```powershell
   pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release -IncludeInstaller
   ```

2. Build and sign with an installed certificate:

   ```powershell
   pwsh .\scripts\Sign-Release.ps1 -CertificateThumbprint <thumbprint>
   ```

   Or import an external PFX from outside the repository:

   ```powershell
   pwsh .\scripts\Sign-Release.ps1 -CertificatePath "C:\Path\To\OnlyRag-CodeSigning.pfx"
   ```

   `Sign-Release.ps1` runs non-invasive signed release verification unless
   `-SkipReleaseVerification` is supplied.

3. Run lifecycle verification on a clean Windows profile or verification machine:

   ```powershell
   pwsh .\scripts\Test-InstallerRelease.ps1 -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe -RequireSigned -RunInstallLifecycle
   ```

Production release readiness requires package gate success, a valid signed installer, lifecycle
evidence, and representative checks for the target OCR/Ollama/Qdrant/image generation runtime scope
plus translation PDF export through LibreOffice.

## Runtime Configuration

Required environment variables: none.

Optional environment variables:

- `ONLYRAG_WEB_DEV_SERVER`: Debug-only WebView2 source override. Only loopback `http` or `https`
  URLs without embedded credentials are accepted.
- `ONLYRAG_LIBREOFFICE_PATH`: full path to `soffice.exe` when LibreOffice for translation PDF
  export is outside standard Windows install locations.

## Local Paths

- `%LOCALAPPDATA%\OnlyRag`: documents, SQLite state, Qdrant local storage, jobs, settings, chat
  history, OCR cache, logs, WebView2 profile data, and exports.
- `%LOCALAPPDATA%\OnlyRag\backups`: timestamped reset backups. A confirmed full data reset creates
  `reset-YYYYMMDDTHHMMSSZ` here immediately before deleting the local runtime contents.
- `%LOCALAPPDATA%\Programs\OnlyRag`: default installed app path.

## Reset Backup Restore

The Settings full data reset is destructive only after explicit UI confirmation. On the next
startup, before deleting local data, OnlyRag copies the current `%LOCALAPPDATA%\OnlyRag` contents
except existing backups into `%LOCALAPPDATA%\OnlyRag\backups\reset-YYYYMMDDTHHMMSSZ`.

To restore manually:

1. Close OnlyRag.
2. Move the current `%LOCALAPPDATA%\OnlyRag` contents aside, keeping the `backups` folder.
3. Copy the desired backup folder contents back into `%LOCALAPPDATA%\OnlyRag`.
4. Start OnlyRag and check Settings > Diagnostics and Storage status.

Ignored generated repository outputs:

- `src\OnlyRag.Web\dist`
- `src\OnlyRag.Web\node_modules`
- project `bin` and `obj` folders
- `packaging\qdrant\payload`
- `artifacts`
- frontend and Playwright test output folders

Use cleanup after local verification when outputs are no longer needed:

```powershell
pwsh .\scripts\Clean.ps1
```

`Clean.ps1` removes generated outputs and ignored dependencies/artifacts by default. It does not
revert tracked source changes.

## Troubleshooting

- Missing .NET SDK: install the official .NET 10 SDK and verify with `dotnet --list-sdks`.
- Missing Node/npm: install official Node.js 20.19.x or 22.12+ and verify with `node --version`
  and `npm --version`.
- Missing WebView2 Runtime: install Microsoft Edge WebView2 Evergreen Runtime and verify from
  Windows Settings > Apps or by locating `msedgewebview2.exe` under
  `Program Files\Microsoft\EdgeWebView\Application`.
- Frontend e2e tests cannot launch a browser: install or repair Microsoft Edge, then rerun
  `npm run test` from `src\OnlyRag.Web`.
- Missing Inno Setup: install Inno Setup 6 and verify with `ISCC.exe /?`, or pass
  `-InnoSetupCompiler` where supported.
- Missing signing tools: install Windows 10/11 SDK and verify with `signtool.exe /?`, or pass
  `-SignToolPath` where supported.
- Installer lifecycle blocked: verify on a clean Windows profile or machine with WebView2 installed
  and pass the exact signed installer path to `Test-InstallerRelease.ps1`.
- OCR provisioning skipped or unavailable: install Python 3.10, 3.11, 3.12, or 3.13, then rerun
  bootstrap or use the OCR action in Settings.
- Ollama unavailable: install/start Ollama or configure a trusted LAN endpoint in Settings.
- Image generation unavailable: download the selected integrated model from Images, confirm SHA256
  verification, and retry. DirectML GPU is preferred when available; CPU fallback is supported.
- LibreOffice unavailable: install LibreOffice or set `ONLYRAG_LIBREOFFICE_PATH` to enable
  translation PDF export.
