# Packaging

OnlyRag uses an Inno Setup 6 installer for Windows desktop packaging.

General setup, build, test, run, and troubleshooting commands are maintained in
[`docs\OPERATIONS.md`](../docs/OPERATIONS.md). This document covers installer-specific behavior.

Build the installer from PowerShell 7:

```powershell
pwsh .\scripts\Build-Installer.ps1
```

The script runs the React/Vite build first, publishes the WPF app with `dotnet publish`, validates the publish payload, and then compiles `packaging\OnlyRag.iss` with Inno Setup when `ISCC.exe` is available.
Pass a real code-signing certificate thumbprint to produce a signed release candidate:

```powershell
pwsh .\scripts\Build-Installer.ps1 -SigningCertificateThumbprint "<SHA1 thumbprint>"
```

When signing is enabled, the build signs the installer with SHA-256, applies an RFC 3161 timestamp, and runs `signtool verify /pa /v` before returning the artifact.
For the complete certificate-folder and scripted signing pipeline, see [`docs\SIGNING.md`](../docs/SIGNING.md).

## Install Model

- Installer technology: Inno Setup 6.
- Install scope: per-user.
- Privileges: no elevation requested.
- Installation directory: `%LOCALAPPDATA%\Programs\OnlyRag`.
- Runtime identifier: `win-x64`.
- Publish mode: framework-dependent; Microsoft runtimes are prerequisites and are not bundled.

## Prerequisites

Required before installation:

- Windows 10 1809 or newer.
- .NET 10 x64 runtime.
- .NET 10 Windows Desktop x64 runtime.
- .NET 10 ASP.NET Core x64 runtime.
- Microsoft Edge WebView2 Runtime.

External and configurable:

- Ollama is not bundled. Install and configure it separately for model features.
- OCR/PaddleOCR runtime packages and OCR models are optional and are not bundled in the installer. The supported strategy is a per-user Python virtual environment under `%LOCALAPPDATA%\OnlyRag\ocr-python`, prepared from **Configura OCR** in Settings or by the developer bootstrap from pinned `scripts\ocr\requirements.txt` versions and verified against `scripts\ocr\runtime-manifest.json`.

Build-time prerequisites:

- PowerShell 7.
- .NET 10 SDK.
- Node.js matching `^20.19.0 || >=22.12.0` with npm for `src\OnlyRag.Web`.
- Inno Setup 6 to generate the installer executable.
- Windows SDK `signtool.exe` and a trusted code-signing certificate with private key to sign release candidates.

## Included

- Published WPF desktop app.
- In-process backend assemblies.
- React static assets under `wwwroot`.
- Required application assemblies and native runtime assets from `dotnet publish`.
- OCR bridge scripts copied by the existing project file.

## Not Included

- User data under `%LOCALAPPDATA%\OnlyRag`.
- SQLite databases or imported document storage.
- Imported documents, OCR cache, exports, logs, or temp files.
- Ollama models or Ollama runtime.
- PaddleOCR Python environment, OCR packages, or OCR models. PaddleOCR may download models on first OCR use into the user profile cache; keep at least 5 GB free for OCR packages and models.
- Signing certificate.

## Verification Status

`scripts\Build-Installer.ps1` verifies React build, `dotnet publish`, basic publish payload completeness, and absence of known user-data paths in the publish output. Installer generation is verified only when Inno Setup is installed.

Installation, upgrade, uninstall, rollback/downgrade, optional component status, and signing evidence are verified by:

```powershell
pwsh .\scripts\Test-InstallerRelease.ps1 -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe
pwsh .\scripts\Test-InstallerRelease.ps1 -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe -RequireSigned -RunInstallLifecycle
```

The first command is non-invasive and produces a JSON evidence artifact under `artifacts\release-verification`. The second command enforces a valid signature and executes the installer lifecycle on the current Windows user profile, so run it only on a clean release verification machine.

## Pre-Release Checklist

Execute these through `scripts\Test-InstallerRelease.ps1 -RunInstallLifecycle` on a clean Windows 10/11 x64 machine before tagging a release.

**Prerequisites**

- [ ] .NET 10 runtime, Windows Desktop Runtime, and ASP.NET Core Runtime are installed
- [ ] Microsoft Edge WebView2 Runtime is installed
- [ ] No previous OnlyRag installation is present

**Fresh install**

- [ ] Run the installer; no UAC elevation prompt appears
- [ ] App installs to `%LOCALAPPDATA%\Programs\OnlyRag`
- [ ] App launches from Start Menu shortcut
- [ ] App launches from desktop shortcut (if selected during install)
- [ ] App starts, health endpoint responds, and UI loads

**Upgrade**

- [ ] Run a newer-versioned installer over the existing installation
- [ ] Installer completes without error
- [ ] App starts with the new version; previous user data under `%LOCALAPPDATA%\OnlyRag` is preserved

**Uninstall**

- [ ] Uninstall via Settings > Apps or the Start Menu uninstall entry
- [ ] `%LOCALAPPDATA%\Programs\OnlyRag` is removed
- [ ] Start Menu and desktop shortcuts are removed
- [ ] User data under `%LOCALAPPDATA%\OnlyRag` is preserved (not removed by uninstaller)

**Rollback**

- [ ] Install an older version after a newer one; installer completes without error
- [ ] Older app version runs correctly

**Signing**

- [ ] Installer `.exe` is signed with a trusted code-signing certificate before release
- [ ] `signtool verify /pa /v OnlyRag-Setup-*.exe` reports a valid signature and no warnings
- [ ] Windows SmartScreen does not block the signed installer on a machine that has not seen it before
