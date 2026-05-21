# OnlyRag

<p align="center">
  <img src=".github/assets/onlyrag-logo-horizontal.png" width="360" alt="OnlyRag">
</p>

[![CI](https://github.com/dennidalpos/OnlyRag/actions/workflows/ci.yml/badge.svg)](https://github.com/dennidalpos/OnlyRag/actions/workflows/ci.yml)

OnlyRag is a Windows desktop app for building a local document library and using it with
Ollama-backed search, chat, OCR, and translation workflows.

The app is local-first: documents, indexes, jobs, settings, chat history, OCR cache, logs, and
exports live under `%LOCALAPPDATA%\OnlyRag`. Ollama can run locally or on a trusted LAN endpoint.
For RAG answers, OnlyRag sends retrieved snippets to the model, not full source documents.

## What Works

- Import TXT, Markdown, CSV, PDF, DOCX, XLSX, PPTX, and image files into a local library.
- Convert `.doc`, `.xls`, and `.ppt` files through optional LibreOffice.
- Run OCR for scanned PDFs and images through the PaddleOCR bridge when Python OCR prerequisites
  are prepared.
- Generate embeddings through a configured Ollama endpoint and store vectors locally in SQLite.
- Search selected documents with hybrid keyword and sqlite-vec vector retrieval.
- Chat with selected documents and show source snippets for grounded answers.
- Translate indexed documents, edit page-based translation units, and export TXT, Markdown, HTML,
  DOCX, or PDF output.
- Configure Ollama endpoint, chat/embedding/translation models, `num_ctx` behavior, ingestion,
  OCR, Office conversion, and performance settings from the desktop UI.
- Track ingestion, embedding, OCR, and translation jobs in the desktop UI.
- Confirm app exit when local jobs or unsaved UI work exist; confirmed exit saves available work,
  cancels active local jobs, and stops the in-process backend.
- Build, test, publish, sign, and package Windows installer candidates with repository scripts.

## Requirements

Development:

- Windows 10 1809 or newer.
- PowerShell 7 (`pwsh`).
- .NET 10 SDK.
- Node.js `^20.19.0 || >=22.12.0` with npm.
- Microsoft Edge WebView2 Runtime.

Model features:

- Ollama, reachable locally or on a trusted LAN endpoint.

Optional features:

- LibreOffice for legacy Office conversion and PDF export.
- Python 3.10+ for the PaddleOCR bridge.
- Inno Setup 6 for installer generation.
- Windows SDK `signtool.exe` and a trusted code-signing certificate for signed release candidates.

Installed app:

- Windows 10 1809 or newer, or Windows 11.
- Microsoft Edge WebView2 Runtime. The installer blocks before copying the app when WebView2 is missing and shows the official Microsoft install/verify instructions. Direct app launch also checks this before loading the UI.
- The installer package is self-contained for the required .NET runtime components and includes the required `sqlite-vec` native asset; end users do not need to install .NET separately.

## Fresh Install

From a clean checkout on Windows, run PowerShell 7 from the repository root:

```powershell
pwsh .\scripts\Bootstrap-Prerequisites.ps1
pwsh .\scripts\Build-Web.ps1
dotnet run --project .\src\OnlyRag.App\OnlyRag.App.csproj --configuration Debug
```

The bootstrap verifies prerequisites, creates `%LOCALAPPDATA%\OnlyRag`, restores .NET packages,
installs web dependencies with `npm ci`, and prepares the optional OCR Python environment when
Python is available. `Build-Web.ps1` produces the static UI consumed by the WPF app when the Vite
development server is not running.

To verify a fresh checkout before packaging or handoff:

```powershell
pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release
```

End users configure optional dependencies from **Settings** in the app:

- **Ollama**: if missing, the UI can start the official PowerShell install command
  `irm https://ollama.com/install.ps1 | iex`.
- **LibreOffice**: if missing, the UI opens the LibreOffice download page.
- **OCR**: the **Configura OCR** button prepares the per-user PaddleOCR environment under
  `%LOCALAPPDATA%\OnlyRag\ocr-python`.

## Commands

Build the web UI:

```powershell
pwsh .\scripts\Build-Web.ps1
```

Build the .NET solution:

```powershell
pwsh .\scripts\Build-App.ps1 -Configuration Release
```

Run the canonical repository gate:

```powershell
pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release
```

The gate includes npm production dependency audit and NuGet transitive vulnerability audit.

Run web lint and formatter checks directly:

```powershell
Set-Location .\src\OnlyRag.Web
npm run typecheck
npm run lint
npm run format:check
```

Run the desktop app:

```powershell
dotnet run --project .\src\OnlyRag.App\OnlyRag.App.csproj --configuration Debug
```

Build an unsigned installer:

```powershell
pwsh .\scripts\Build-Installer.ps1 -Configuration Release
```

Check installer prerequisite messaging:

```powershell
pwsh .\scripts\Test-InstallerPrerequisites.ps1 -SelfTest
```

Create installer evidence without installing:

```powershell
pwsh .\scripts\Test-InstallerRelease.ps1 -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe
```

The repository gate runs web lint and formatter checks before tests and builds.

## Project Status

OnlyRag is an implementation-stage Windows desktop application. The repository supports setup,
dependency install, web build, .NET build, tests, local run, unsigned installer packaging, scripted
release signing, and non-invasive installer evidence generation.

Signed release completion is blocked until a trusted Windows code-signing certificate is provided
and full installer lifecycle verification is run on a clean Windows verification machine. Real
residual work is tracked in [PROJECT_STATUS.json](PROJECT_STATUS.json).

## Documentation

- [Documentation index](docs/README.md)
- [Operations](docs/OPERATIONS.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Scripts](scripts/README.md)
- [Brand assets](docs/BRAND_ASSETS.md)
- [RAG pipeline](docs/RAG_PIPELINE.md)
- [OCR pipeline](docs/OCR_PIPELINE.md)
- [Office ingestion](docs/OFFICE_INGESTION.md)
- [Translation pipeline](docs/TRANSLATION_PIPELINE.md)
- [Signing](docs/SIGNING.md)
- [Packaging](packaging/README.md)
