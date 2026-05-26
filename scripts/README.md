# Scripts

Run repository scripts from the repository root with PowerShell 7 unless noted otherwise.

## Canonical Flows

```powershell
pwsh .\scripts\Bootstrap-Prerequisites.ps1
pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release
pwsh .\scripts\Build-App.ps1 -Configuration Release
dotnet run --project .\src\OnlyRag.App\OnlyRag.App.csproj --configuration Debug
```

Installer packaging and signed release verification require additional local tools:

```powershell
pwsh .\scripts\Build-Installer.ps1 -Configuration Release
pwsh .\scripts\Sign-Release.ps1 -CertificateThumbprint <thumbprint>
pwsh .\scripts\Test-InstallerRelease.ps1 -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe -RequireSigned -RunInstallLifecycle
```

Generated outputs can be removed with:

```powershell
pwsh .\scripts\Clean.ps1
```

| script | path | purpose | when to use | called by | prerequisites | outputs | notes |
|---|---|---|---|---|---|---|---|
| Bootstrap prerequisites | `scripts\Bootstrap-Prerequisites.ps1` | Verify local prerequisites, restore .NET packages, install web dependencies, and prepare optional OCR runtime. | Fresh checkout setup or dependency repair. | Manual. | Windows, PowerShell 7, .NET 10 SDK; Node/npm unless `-SkipNode`; optional Python/Ollama/LibreOffice. | `%LOCALAPPDATA%\OnlyRag`, restored packages, `src\OnlyRag.Web\node_modules`, optional OCR env. | Does not build, package, deploy, sign, or release. |
| Build web UI | `scripts\Build-Web.ps1` | Install web dependencies when needed and run the Vite build. | Before desktop build or when frontend changes. | `Build-App.ps1`, `Invoke-Gate.ps1`, `Build-Installer.ps1`. | Node/npm matching `src\OnlyRag.Web\package.json`. | `src\OnlyRag.Web\dist`. | `-SkipInstallWhenUpToDate` avoids npm install when modules are current. |
| Build app | `scripts\Build-App.ps1` | Build web assets if needed, ensure Qdrant payload, restore .NET unless skipped, and build the solution. | Local .NET build. | `Invoke-Gate.ps1`. | .NET 10 SDK, Node/npm unless `-SkipWebBuild` is safe. | .NET build outputs under project `bin` directories. | Use `-SkipWebBuild` only when `dist\index.html` already exists. |
| Repository gate | `scripts\Invoke-Gate.ps1` | Run the canonical verification gate. | Before handoff, release candidate work, or CI parity checks. | CI workflow. | Windows, PowerShell 7, .NET 10 SDK, Node/npm; Inno Setup only with `-IncludeInstaller`. | Gate console summary; build outputs; optional installer. | `-ContinueOnError` keeps independent checks running for diagnostics. |
| Build installer | `scripts\Build-Installer.ps1` | Ensure Qdrant payload, publish self-contained app, and compile the Inno Setup installer. | Packaging candidate build. | `Invoke-Gate.ps1 -IncludeInstaller`, `Sign-Release.ps1`. | .NET 10 SDK, Node/npm, Inno Setup 6, WebView2/brand assets; network access when Qdrant payload is missing. | `packaging\qdrant\payload`, `artifacts\publish\OnlyRag\win-x64`, `artifacts\installer\*.exe`. | Signing is optional via `-SigningCertificateThumbprint`; unsigned output is not release-ready. |
| Sign release | `scripts\Sign-Release.ps1` | Build, sign, and verify a release installer. | Release candidate signing. | Manual. | Inno Setup 6, Windows SDK `signtool.exe`, trusted certificate. | Signed installer under `artifacts\installer`. | PFX input must be outside the repository. |
| Test installer release | `scripts\Test-InstallerRelease.ps1` | Produce installer evidence and optionally run install/launch/uninstall lifecycle. | Release verification. | `Sign-Release.ps1` unless skipped. | Built installer; clean Windows profile for `-RunInstallLifecycle`. | `artifacts\release-verification\*.json` plus installer logs. | Non-lifecycle mode is non-invasive. |
| Test installer prerequisites | `scripts\Test-InstallerPrerequisites.ps1` | Validate installer prerequisite messaging and detection logic. | Gate and installer messaging changes. | `Invoke-Gate.ps1`. | PowerShell 7. | Console result. | `-SelfTest` runs supported/missing simulation cases. |
| Download Qdrant | `scripts\Download-Qdrant.ps1` | Download and verify bundled Qdrant payload from manifest. | When payload is missing or manifest changes. | `Build-App.ps1`, `Build-Installer.ps1`. | Network access; manifest SHA values. | `packaging\qdrant\payload`. | `-Force` refreshes existing payload. |
| Clean generated outputs | `scripts\Clean.ps1` | Remove generated build/test outputs and optionally artifacts/dependencies. | Local cleanup. | Manual. | PowerShell 7. | Removed generated files/directories. | Supports `-WhatIf`; does not revert source changes. |
| Generate brand assets | `scripts\Generate-BrandAssets.ps1` | Regenerate logo, social, setup, web, and GitHub assets. | After changing source brand SVG. | Manual. | Windows, PowerShell 7, WPF imaging assemblies. | `assets\brand`, `src\OnlyRag.Web\public`, `.github\assets`. | Updates `assets\brand\manifest.json`. |
| Export signing certificate | `scripts\Export-EnterpriseSigningCertificate.ps1` | Export a public certificate for enterprise trust distribution. | Enterprise deployment preparation. | Manual. | Existing installer or installed/external certificate. | `certificates\app\OnlyRag-Enterprise-CodeSigning.cer` by default. | Refuses private signing material inside the repository. |
| Test enterprise signing trust | `scripts\Test-EnterpriseSigningTrust.ps1` | Check certificate store and optional installer trust/signature state. | Enterprise signing validation. | Manual. | Certificate thumbprint; optional installer. | Console trust report. | Use after signing or certificate distribution changes. |
| OCR runtime install | `scripts\ocr\install_ocr_runtime.ps1` | Prepare PaddleOCR Python runtime from the manifest. | Installer preinstall, Settings OCR provisioning, or manual OCR repair. | Installer and app setup flows. | Python 3.10-3.13, Internet access for package download. | `%LOCALAPPDATA%\OnlyRag\ocr-python`. | `-RuntimeTarget auto` selects CPU/GPU from local capability. |
| OCR manifest test | `scripts\ocr\Test-OcrRuntimeManifest.ps1` | Validate OCR runtime manifest structure. | Gate and manifest edits. | `Invoke-Gate.ps1`. | PowerShell 7. | Console result. | Fast local check. |
| OCR catalog test | `scripts\ocr\Test-OcrRuntimeCatalog.ps1` | Probe reachable PaddlePaddle runtime packages and report catalog drift. | Scheduled maintenance or runtime updates. | OCR catalog workflow. | Python and network access. | JSON report at supplied `-OutputPath`. | Exit code 2 indicates reachable runtime not represented in manifest. |

Support scripts under `scripts\support` are internal helpers for public scripts and should not be
treated as stable user-facing commands.
