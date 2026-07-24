# Scripts

Run public repository scripts from the repository root with PowerShell 7 (`pwsh`). Support scripts
under `scripts\support` are internal helpers and are not stable user-facing commands.

## Canonical Flows

Setup:

```powershell
pwsh .\scripts\Bootstrap-Prerequisites.ps1
```

Application readiness check:

```powershell
pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release
```

Local build and start:

```powershell
pwsh .\scripts\Build-Web.ps1
pwsh .\scripts\Build-App.ps1 -Configuration Release
dotnet run --project .\src\OnlyRag.App\OnlyRag.App.csproj --configuration Debug
```

Frontend development server:

```powershell
Set-Location .\src\OnlyRag.Web
npm run dev
```

Package, sign, and verify:

```powershell
pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release -IncludeInstaller
pwsh .\scripts\Sign-Release.ps1 -CertificateThumbprint <thumbprint>
pwsh .\scripts\Test-InstallerRelease.ps1 -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe -RequireSigned -RunInstallLifecycle
```

The package gate only proves the installer can be compiled on the current Windows machine. Release
readiness also requires a valid signature, lifecycle verification, and representative runtime
checks for the intended deployment scope.

Cleanup:

```powershell
pwsh .\scripts\Clean.ps1
```

## Public Script Inventory

| script | path | purpose | when to use | called by | prerequisites | outputs | notes |
|---|---|---|---|---|---|---|---|
| Format code | `scripts\Format-Code.ps1` | Format .NET C# solution and Web frontend (Prettier). | Routine development, pre-commit formatting. | Manual. | PowerShell 7, .NET 10 SDK, Node/npm. | Formatted source code. | Supports `-CheckOnly` for verification without mutating files. |
| Lint code | `scripts\Lint-Code.ps1` | Run ESLint, TypeScript typecheck, and .NET analyzer checks. | Code quality verification. | Manual. | PowerShell 7, .NET 10 SDK, Node/npm. | Console analysis report. | Fails on any lint or type error. |
| Test code | `scripts\Test-Code.ps1` | Run full Vitest frontend component tests and .NET xUnit solution tests. | Automated test execution. | Manual. | PowerShell 7, .NET 10 SDK, Node/npm. | Test output & pass/fail status. | Supports `-IncludeE2e` to run Playwright E2E suite. |
| Bootstrap prerequisites | `scripts\Bootstrap-Prerequisites.ps1` | Verify Windows development prerequisites, restore .NET packages, install web dependencies, check optional local endpoints, prepare integrated image model storage, and prepare optional OCR runtime. | Fresh checkout setup or dependency repair. | Manual. | Windows, PowerShell 7, .NET 10 SDK; Node/npm unless `-SkipNode`; optional Python/Ollama/LibreOffice. | `%LOCALAPPDATA%\OnlyRag`, `%LOCALAPPDATA%\OnlyRag\models\images`, restored packages, `src\OnlyRag.Web\node_modules`, optional OCR env. | Use `-SkipImageGenerationCheck` to skip image model storage checks. Does not build, package, sign, install, deploy, or release. |
| Build web UI | `scripts\Build-Web.ps1` | Install web dependencies when needed and run the Vite production build. | Before desktop build or when frontend changes. | `Build-App.ps1`, `Invoke-Gate.ps1`, `Build-Installer.ps1`. | Node/npm matching `src\OnlyRag.Web\package.json`. | `src\OnlyRag.Web\dist`. | `-SkipInstallWhenUpToDate` avoids dependency install when `node_modules` is current. |
| Build app | `scripts\Build-App.ps1` | Build web assets, prepare Qdrant payload when missing, restore .NET unless skipped, and build the desktop app for `win-x64` by default. | Local desktop build. | `Invoke-Gate.ps1`. | .NET 10 SDK; Node/npm unless `-SkipWebBuild` is safe; network access when Qdrant payload is missing. | Web `dist`, Qdrant payload, runtime-specific .NET `bin` outputs. | Use `-SkipWebBuild` only after `src\OnlyRag.Web\dist\index.html` exists. |
| Repository gate | `scripts\Invoke-Gate.ps1` | Run the canonical verification gate. | Before handoff, release candidate work, or CI parity checks. | CI workflow. | Windows, PowerShell 7, .NET 10 SDK, Node/npm; NSIS only with `-IncludeInstaller`. | Console gate summary, restored dependencies, build outputs, optional installer. | `-ContinueOnError` keeps independent checks running for diagnostics. |
| Build installer | `scripts\Build-Installer.ps1` | Build web assets, prepare Qdrant, publish self-contained `win-x64` app, verify native runtime payloads, and compile the NSIS installer. | Packaging candidate build. | `Invoke-Gate.ps1 -IncludeInstaller`, `Sign-Release.ps1`. | .NET 10 SDK, Node/npm, NSIS 3.x, installer brand assets; network access when Qdrant payload is missing. | `packaging\qdrant\payload`, `artifacts\publish\OnlyRag\win-x64`, `artifacts\installer\*.exe`. | Fails if DirectML/ONNX Runtime native files are missing. `-SigningCertificateThumbprint` signs the installer during build; unsigned output is not release-ready. |
| Sign release | `scripts\Sign-Release.ps1` | Build, sign, and non-invasively verify a release installer. | Release candidate signing. | Manual. | NSIS 3.x, Windows SDK `signtool.exe`, trusted certificate. | Signed installer under `artifacts\installer`; release evidence unless skipped. | PFX input must be outside the repository. Temporarily imported certificates are removed unless `-KeepImportedCertificate` is supplied. |
| Test installer release | `scripts\Test-InstallerRelease.ps1` | Produce installer evidence and optionally run install/launch/uninstall lifecycle. | Release verification. | `Sign-Release.ps1` in non-lifecycle mode unless skipped. | Built installer; clean Windows profile or verification machine for `-RunInstallLifecycle`. | `artifacts\release-verification\*.json` plus installer logs. | Non-lifecycle mode is non-invasive. `-RequireSigned` fails invalid signatures. |
| Test installer prerequisites | `scripts\Test-InstallerPrerequisites.ps1` | Validate installer prerequisite messaging and detection logic. | Gate and installer messaging changes. | `Invoke-Gate.ps1`. | PowerShell 7. | Console result. | `-SelfTest` runs supported/missing simulation cases. |
| Download Qdrant | `scripts\Download-Qdrant.ps1` | Download and verify bundled Qdrant payload from manifest. | When payload is missing or manifest changes. | `Build-App.ps1`, `Build-Installer.ps1`. | Network access; valid manifest URLs and SHA values. | `packaging\qdrant\payload`. | `-Force` refreshes existing payload. |
| Clean generated outputs | `scripts\Clean.ps1` | Remove generated build/test outputs, dependencies, and artifacts by default. | Local cleanup after verification. | Manual. | PowerShell 7. | Removed generated files/directories. | Supports `-WhatIf`, `-PreserveArtifacts`, `-PreserveDependencies`, `-IncludeArtifacts`, and `-IncludeDependencies`; does not revert source changes. |
| Generate brand assets | `scripts\Generate-BrandAssets.ps1` | Regenerate logo, social, setup, web, and GitHub assets. | After changing source brand SVG/assets. | Manual. | Windows, PowerShell 7, WPF imaging assemblies. | `assets\brand`, `src\OnlyRag.Web\public`, `.github\assets`. | Updates `assets\brand\manifest.json`. |
| Export signing certificate | `scripts\Export-EnterpriseSigningCertificate.ps1` | Export a public certificate for enterprise trust distribution. | Enterprise deployment preparation. | Manual. | Existing installer, installed certificate, or external public certificate source. | `certificates\app\OnlyRag-Enterprise-CodeSigning.cer` by default. | Refuses private signing material inside the repository. |
| Test enterprise signing trust | `scripts\Test-EnterpriseSigningTrust.ps1` | Check certificate store and optional installer trust/signature state. | Enterprise signing validation. | Manual. | Certificate thumbprint; optional installer. | Console trust report. | Use after signing or certificate distribution changes. |
| Evaluate retrieval | `scripts\Evaluate-Retrieval.ps1` | Evaluate local retrieval cases with expected chunks, recall@k, MRR, and context-size metrics. | RAG tuning, regression checks, and handoff evidence for retrieval changes. | Manual. | PowerShell 7; optional running backend and session token when live-searching. | JSON report under `artifacts\retrieval-evaluation` by default. | Use `docs\retrieval-evaluation.sample.json` as the dataset shape. |
| OCR runtime install | `scripts\ocr\install_ocr_runtime.ps1` | Prepare PaddleOCR Python runtime from the manifest. | Installer preinstall, Settings OCR provisioning, or manual OCR repair. | Installer and app setup flows. | Python 3.10-3.13, Internet access for package download. | `%LOCALAPPDATA%\OnlyRag\ocr-python`. | `-RuntimeTarget auto` selects CPU/GPU from local capability. |
| OCR manifest test | `scripts\ocr\Test-OcrRuntimeManifest.ps1` | Validate OCR runtime manifest structure. | Gate and manifest edits. | `Invoke-Gate.ps1`. | PowerShell 7. | Console result. | Fast local check. |
| OCR catalog test | `scripts\ocr\Test-OcrRuntimeCatalog.ps1` | Probe reachable PaddlePaddle runtime packages and report catalog drift. | Scheduled maintenance or runtime updates. | OCR catalog workflow. | Python and network access. | JSON report at supplied `-OutputPath`. | Exit code 2 indicates reachable runtime not represented in manifest. |

## Direct Non-Script Checks

Frontend checks:

```powershell
Set-Location .\src\OnlyRag.Web
npm run typecheck
npm run lint
npm run format:check
npm run test
```

.NET tests:

```powershell
dotnet test .\OnlyRag.sln --configuration Release
```

These direct checks are useful during focused development. The canonical handoff check remains
`pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release`.
