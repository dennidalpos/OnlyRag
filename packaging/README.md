# Packaging

OnlyRag uses Inno Setup 6 to package the self-contained Windows desktop app.

## Inputs

- [`OnlyRag.iss`](OnlyRag.iss): Inno Setup script.
- [`../src/OnlyRag.App/OnlyRag.App.csproj`](../src/OnlyRag.App/OnlyRag.App.csproj): WPF app
  publish target.
- [`../src/OnlyRag.Web/dist/index.html`](../src/OnlyRag.Web/dist/index.html): web UI build
  output required before desktop packaging.
- [`qdrant/manifest.json`](qdrant/manifest.json): bundled Qdrant runtime metadata.
- [`qdrant/payload/qdrant.exe`](qdrant/payload/qdrant.exe): bundled Qdrant executable, prepared
  by [`../scripts/Download-Qdrant.ps1`](../scripts/Download-Qdrant.ps1). The installer build
  script runs this automatically when the payload is missing.
- [`../assets/brand/setup`](../assets/brand/setup): installer wizard images.

## Build

Unsigned installer:

```powershell
pwsh .\scripts\Build-Installer.ps1 -Configuration Release
```

The script builds the React/Vite UI, prepares the Qdrant payload when needed, publishes the WPF app
self-contained for `win-x64`, verifies the publish payload, and compiles `packaging\OnlyRag.iss`
with Inno Setup. Default outputs:

- `artifacts\publish\OnlyRag\win-x64`
- `artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe`

Signed installer:

```powershell
pwsh .\scripts\Sign-Release.ps1 -CertificateThumbprint <thumbprint>
```

See [Signing](../docs/SIGNING.md) for certificate handling.

The normal CI workflow does not compile the installer. Package readiness requires
`pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release -IncludeInstaller` or
`pwsh .\scripts\Build-Installer.ps1 -Configuration Release` on a Windows machine with Inno Setup 6.

## Installer Behavior

The installer:

- Requires Windows 10 version 1809/build 17763 or newer, or Windows 11.
- Blocks installation when Microsoft Edge WebView2 Runtime is missing.
- Installs under `%LOCALAPPDATA%\Programs\OnlyRag`.
- Preserves user data under `%LOCALAPPDATA%\OnlyRag` on uninstall.
- Includes the self-contained .NET runtime payload, WebView2 assemblies, SQLite native provider,
  Qdrant payload, OCR scripts, OCR requirements, and bundled web UI.
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
