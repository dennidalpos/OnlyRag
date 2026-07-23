---
name: onlyrag
description: Primary development skill for OnlyRag. Covers overall architecture, WPF/WebView2 backend bridge, PowerShell automation scripts, local data paths, release verification gates, and code conventions.
---

# OnlyRag — Primary Project Skill

This skill provides comprehensive operational and architectural guidance for developing, testing, packaging, and verifying the OnlyRag application.

## 1. Official Documentation & References

- **OnlyRag Documentation Index**: [docs/README.md](file:///d:/GITHUB/OnlyRag/docs/README.md)
- **Architecture**: [docs/ARCHITECTURE.md](file:///d:/GITHUB/OnlyRag/docs/ARCHITECTURE.md)
- **Operations & Handoff**: [docs/OPERATIONS.md](file:///d:/GITHUB/OnlyRag/docs/OPERATIONS.md)
- **Application Flow**: [docs/APP_FLOW.md](file:///d:/GITHUB/OnlyRag/docs/APP_FLOW.md)
- **Scripts Inventory**: [scripts/README.md](file:///d:/GITHUB/OnlyRag/scripts/README.md)
- **RAG Pipeline**: [docs/RAG_PIPELINE.md](file:///d:/GITHUB/OnlyRag/docs/RAG_PIPELINE.md)
- **OCR Pipeline**: [docs/OCR_PIPELINE.md](file:///d:/GITHUB/OnlyRag/docs/OCR_PIPELINE.md)
- **Image Generation**: [docs/IMAGE_GENERATION.md](file:///d:/GITHUB/OnlyRag/docs/IMAGE_GENERATION.md)
- **Office Ingestion**: [docs/OFFICE_INGESTION.md](file:///d:/GITHUB/OnlyRag/docs/OFFICE_INGESTION.md)
- **Translation Pipeline**: [docs/TRANSLATION_PIPELINE.md](file:///d:/GITHUB/OnlyRag/docs/TRANSLATION_PIPELINE.md)
- **Packaging**: [packaging/README.md](file:///d:/GITHUB/OnlyRag/packaging/README.md)
- **Signing**: [docs/SIGNING.md](file:///d:/GITHUB/OnlyRag/docs/SIGNING.md)

## 2. Core Architecture

OnlyRag is a local-first Windows desktop app combining:
1. **WPF Desktop Shell** ([`src/OnlyRag.App`](file:///d:/GITHUB/OnlyRag/src/OnlyRag.App)): Hosts Microsoft Edge WebView2 and manages backend startup/shutdown.
2. **React/Vite Web UI** ([`src/OnlyRag.Web`](file:///d:/GITHUB/OnlyRag/src/OnlyRag.Web)): Modern single-page app interface executing inside WebView2.
3. **In-Process Backend API** ([`src/OnlyRag.Api`](file:///d:/GITHUB/OnlyRag/src/OnlyRag.Api)): ASP.NET Core Minimal API hosted in-process inside the WPF app.
4. **Core Contracts** ([`src/OnlyRag.Core`](file:///d:/GITHUB/OnlyRag/src/OnlyRag.Core)): Shared DTOs, interfaces, and settings models.
5. **Infrastructure Adapters** ([`src/OnlyRag.Infrastructure`](file:///d:/GITHUB/OnlyRag/src/OnlyRag.Infrastructure)): SQLite storage, Qdrant vector retrieval, Ollama integration, ONNX image generation, PaddleOCR bridge, and LibreOffice PDF export.
6. **Worker Queue** ([`src/OnlyRag.Worker`](file:///d:/GITHUB/OnlyRag/src/OnlyRag.Worker)): In-process task queue for asynchronous background jobs.

## 3. Directory & Environment Standards

- **Local App Data**: `%LOCALAPPDATA%\OnlyRag` (documents, SQLite database, Qdrant vectors, OCR cache, jobs, settings, logs).
- **Image Models Directory**: `%LOCALAPPDATA%\OnlyRag\models\images`.
- **Installed Target Path**: `%LOCALAPPDATA%\Programs\OnlyRag`.
- **PowerShell Version**: PowerShell 7 (`pwsh`). Run commands from the repository root.

## 4. Primary Command Workflows

### Setup & Prerequisites Bootstrap
```powershell
pwsh .\scripts\Bootstrap-Prerequisites.ps1
```

### Local Build & Start (Production Web Assets)
```powershell
pwsh .\scripts\Build-Web.ps1
dotnet run --project .\src\OnlyRag.App\OnlyRag.App.csproj --configuration Debug
```

### Development Mode (Vite Dev Server + WPF Shell)
In Terminal 1:
```powershell
Set-Location .\src\OnlyRag.Web
npm run dev
```
In Terminal 2:
```powershell
$env:ONLYRAG_WEB_DEV_SERVER = "http://127.0.0.1:5173"
dotnet run --project .\src\OnlyRag.App\OnlyRag.App.csproj --configuration Debug
```

### Canonical Readiness Gate
```powershell
pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release
```
Runs preflight checks, dependency audits, frontend typecheck/lint/format/tests, .NET unit/integration tests, OCR manifest checks, web build, and .NET build.

### Packaging & Release Pipeline
```powershell
# 1. Package Readiness Gate with Installer Compilation
pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release -IncludeInstaller

# 2. Sign Release Installer
pwsh .\scripts\Sign-Release.ps1 -CertificateThumbprint <thumbprint>

# 3. Test Installer Lifecycle
pwsh .\scripts\Test-InstallerRelease.ps1 -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe -RequireSigned -RunInstallLifecycle
```

### Workspace Cleanup
```powershell
pwsh .\scripts\Clean.ps1
```

## 5. Development Guidelines & Rules

- **Windows First**: Use PowerShell 7 (`pwsh`), cross-platform path APIs in code, and relative paths.
- **Cleanliness**: Never check in generated assets (`dist`, `node_modules`, `bin`, `obj`, `artifacts`, `payload`).
- **No Direct Secrets**: Store no certificates or credentials in the repo; use `.env.example` where applicable.
- **Verification Requirement**: Never declare work complete without executing the canonical gate (`Invoke-Gate.ps1 -Configuration Release`).
