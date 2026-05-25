# Operations And Handoff

OnlyRag is a Windows-first desktop app. Use PowerShell 7 from the repository root for local
operations.

## Fresh Install From Checkout

Prerequisites:

- Windows 10 version 1809/build 17763 or newer, or Windows 11.
- PowerShell 7 (`pwsh`).
- .NET 10 SDK. The repository pins SDK selection in [`global.json`](../global.json).
- Node.js `^20.19.0 || >=22.12.0` with npm, matching
  [`src/OnlyRag.Web/package.json`](../src/OnlyRag.Web/package.json).
- Microsoft Edge WebView2 Runtime.
- Optional: Ollama for model features, LibreOffice for legacy Office conversion/PDF export,
  Python 3.10 through 3.13 for OCR provisioning, Inno Setup 6 for installer builds, and Windows
  SDK `signtool.exe` for signing.

Fresh checkout setup:

```powershell
pwsh .\scripts\Bootstrap-Prerequisites.ps1
pwsh .\scripts\Build-Web.ps1
dotnet run --project .\src\OnlyRag.App\OnlyRag.App.csproj --configuration Debug
```

The bootstrap checks the Windows host, PowerShell, .NET, WebView2, Node/npm, optional Ollama,
optional LibreOffice, creates `%LOCALAPPDATA%\OnlyRag`, restores .NET packages, installs web
dependencies, and prepares OCR when supported Python is available.

## Main Verification

Use the repository gate before handoff or release candidate work:

```powershell
pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release
```

The gate performs preflight checks, web dependency restore, .NET restore, npm production audit,
NuGet transitive vulnerability audit, frontend typecheck/lint/format/tests, .NET tests, installer
prerequisite self-test, OCR runtime manifest checks, web build, and .NET build.

Use installer packaging in the gate only when Inno Setup is available and packaging evidence is
needed:

```powershell
pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release -IncludeInstaller
```

## Local Runtime Locations

User data is stored under `%LOCALAPPDATA%\OnlyRag`, including documents, SQLite state, Qdrant
local storage, jobs, settings, chat history, OCR cache, logs, WebView2 profile data, and exports.

Installed application files are placed under `%LOCALAPPDATA%\Programs\OnlyRag` by the Inno Setup
installer. Uninstall preserves user data under `%LOCALAPPDATA%\OnlyRag`.

## Release Handoff

Before publishing an installer:

1. Run `pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release -IncludeInstaller`.
2. Sign the installer with a trusted Windows code-signing certificate:

   ```powershell
   pwsh .\scripts\Sign-Release.ps1 -CertificatePath C:\Path\To\certificate.pfx
   ```

   Or use an installed certificate:

   ```powershell
   pwsh .\scripts\Sign-Release.ps1 -CertificateThumbprint <thumbprint>
   ```

3. Run non-invasive installer evidence:

   ```powershell
   pwsh .\scripts\Test-InstallerRelease.ps1 -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe -RequireSigned
   ```

4. Run full install/launch/uninstall lifecycle on a clean Windows profile or verification machine:

   ```powershell
   pwsh .\scripts\Test-InstallerRelease.ps1 -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe -RequireSigned -RunInstallLifecycle
   ```

Current release residuals are tracked in [`PROJECT_STATUS.json`](../PROJECT_STATUS.json). Do not
treat an unsigned installer or an installer without clean lifecycle verification as release-ready.

## Troubleshooting

- Missing .NET SDK: install the official .NET 10 SDK and verify with `dotnet --list-sdks`.
- Missing Node/npm: install official Node.js 20.19.x or 22.12+ and verify with `node --version`
  and `npm --version`.
- Missing WebView2: install Microsoft Edge WebView2 Evergreen Runtime and verify from Windows
  Settings > Apps or by locating `msedgewebview2.exe` under
  `Program Files\Microsoft\EdgeWebView\Application`.
- Missing Inno Setup: install Inno Setup 6 and verify with `ISCC.exe /?`, or pass
  `-InnoSetupCompiler`.
- Missing signing tools: install Windows 10/11 SDK and verify with `signtool.exe /?`, or pass
  `-SignToolPath`.
- OCR provisioning skipped: install Python 3.10, 3.11, 3.12, or 3.13, then rerun bootstrap or use
  the app Settings OCR action.
- Ollama unavailable: install/start Ollama or configure a trusted LAN endpoint in Settings.
- LibreOffice unavailable: install LibreOffice or set `ONLYRAG_LIBREOFFICE_PATH` for legacy
  `.doc`, `.xls`, and `.ppt` ingestion.
