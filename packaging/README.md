# Packaging

OnlyRag uses NSIS (Nullsoft Scriptable Install System) on Windows to package the self-contained desktop app for `win-x64`.

## Inputs

- [`OnlyRag.nsi`](OnlyRag.nsi): NSIS installer script.
- [`../src/OnlyRag.App/OnlyRag.App.csproj`](../src/OnlyRag.App/OnlyRag.App.csproj): WPF app
  publish target.
- [`../src/OnlyRag.Web/dist/index.html`](../src/OnlyRag.Web/dist/index.html): web UI build
  output required before desktop packaging.
- [`qdrant/manifest.json`](qdrant/manifest.json): bundled Qdrant runtime metadata.
- [`qdrant/payload/qdrant.exe`](qdrant/payload/qdrant.exe): bundled Qdrant executable, prepared
  by [`../scripts/Download-Qdrant.ps1`](../scripts/Download-Qdrant.ps1). The installer build
  script runs this automatically when the payload is missing.

## Build

Unsigned installer:

```powershell
pwsh .\scripts\Build-Installer.ps1 -Configuration Release
```

The script builds the React/Vite UI, prepares the Qdrant payload when needed, publishes the WPF app
self-contained for `win-x64`, verifies the publish payload including DirectML/ONNX Runtime native
files, and compiles `packaging\OnlyRag.nsi` with NSIS (`makensis.exe`). Default outputs:

- `artifacts\publish\OnlyRag\win-x64`
- `artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe`

Signed installer:

```powershell
pwsh .\scripts\Sign-Release.ps1 -CertificateThumbprint <thumbprint>
```

See [Signing](../docs/SIGNING.md) for certificate handling.

The normal CI workflow does not compile the installer. Package build readiness requires:

```powershell
pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release -IncludeInstaller
```

Use `Build-Installer.ps1` directly only for focused packaging work. Neither command makes an
unsigned installer production-ready.

## Installer Behavior

The installer:

- Targets 64-bit Windows (`win-x64`).
- Installs under `%LOCALAPPDATA%\Programs\OnlyRag`.
- Preserves user data under `%LOCALAPPDATA%\OnlyRag` on uninstall.
- Includes the self-contained .NET runtime payload, WebView2 assemblies, SQLite native provider,
  DirectML/ONNX Runtime native files, Qdrant payload, OCR scripts, OCR requirements, and bundled
  web UI.
- Runs OCR runtime preparation when compatible Python and Internet access are available.

## Verification

Non-invasive evidence:

```powershell
pwsh .\scripts\Test-InstallerRelease.ps1 -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe
```

Signed evidence:

```powershell
pwsh .\scripts\Test-InstallerRelease.ps1 -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe -RequireSigned
```

Full lifecycle on a clean Windows profile or verification machine:

```powershell
pwsh .\scripts\Test-InstallerRelease.ps1 -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe -RequireSigned -RunInstallLifecycle
```

Evidence JSON and installer logs are written under `artifacts\release-verification`.

Production release readiness requires package gate success, a valid signed installer, lifecycle
evidence, and representative checks for the target OCR/Ollama/Qdrant runtime scope plus
translation PDF export through LibreOffice.
