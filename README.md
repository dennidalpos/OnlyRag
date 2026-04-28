# OnlyRag

<p align="center">
  <img src=".github/assets/onlyrag-icon.svg" width="96" alt="OnlyRag icon">
</p>

OnlyRag is a Windows desktop app for building a local document library and using it with
Ollama-backed chat, retrieval, OCR, and translation workflows.

The app is local-first. Documents, indexes, jobs, settings, chat history, OCR cache, logs, and
exports live under `%LOCALAPPDATA%\OnlyRag`. Ollama can run locally or on a trusted LAN endpoint;
OnlyRag sends retrieved snippets or translation units, not full documents for RAG answers.

## Verified Features

- Import TXT, Markdown, PDF, DOCX, XLSX, PPTX, and image files into a local library.
- Convert legacy Office files (`.doc`, `.xls`, `.ppt`) through optional LibreOffice.
- Run OCR for scanned PDFs and images through the PaddleOCR bridge when Python OCR prerequisites
  are prepared.
- Generate embeddings through a configured Ollama endpoint and store vectors locally in SQLite.
- Search selected documents with hybrid keyword and sqlite-vec vector retrieval.
- Chat with selected documents and show source snippets for grounded answers.
- Translate indexed documents, edit page-based translation units, and export TXT, Markdown, HTML,
  DOCX, or PDF output.
- Track ingestion, embedding, OCR, and translation jobs in the desktop UI.
- Confirm app exit when local jobs or unsaved UI work exist; confirmed exit saves available work,
  cancels active local jobs, and closes other OnlyRag instances softly.
- Build, test, publish, and package a per-user Windows installer with the repository scripts.

## Minimum Setup

Required for development:

- Windows 10 1809 or newer.
- PowerShell 7 (`pwsh`).
- .NET 10 SDK.
- Node.js matching `^20.19.0 || >=22.12.0` with npm.
- Microsoft Edge WebView2 Runtime.

Required for model features:

- Ollama, reachable locally or on the LAN.

Optional:

- LibreOffice for legacy Office conversion and PDF export.
- Python for the PaddleOCR bridge.
- Inno Setup 6 for installer generation.
- Windows SDK `signtool.exe` and a trusted code-signing certificate for signed release candidates.

## Main Commands

Run commands from the repository root in PowerShell 7.

```powershell
pwsh .\scripts\Bootstrap-Prerequisites.ps1
pwsh .\scripts\Build-Web.ps1
pwsh .\scripts\Build-App.ps1 -Configuration Release
pwsh .\scripts\Test-All.ps1 -Configuration Release
```

Run the desktop app:

```powershell
dotnet run --project .\src\OnlyRag.App\OnlyRag.App.csproj --configuration Debug
```

Build an unsigned installer:

```powershell
pwsh .\scripts\Build-Installer.ps1 -Configuration Release
```

Create installer evidence without installing:

```powershell
pwsh .\scripts\Test-InstallerRelease.ps1 -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe
```

No lint script or formatter configuration is currently defined.

## Project Status

OnlyRag is an implementation-stage Windows desktop application. The repository currently supports
setup, dependency install, web build, .NET build, tests, local run, unsigned installer packaging,
scripted release signing, and non-invasive installer evidence generation.

Signed release completion is blocked until a trusted Windows code-signing certificate is provided
and full installer lifecycle verification is run on a clean Windows verification machine. Open
residual work is tracked in [PROJECT_STATUS.json](PROJECT_STATUS.json).

## Technical Documentation

- [Documentation index](docs/README.md)
- [Operations](docs/OPERATIONS.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Scripts](scripts/README.md)
- [RAG pipeline](docs/RAG_PIPELINE.md)
- [OCR pipeline](docs/OCR_PIPELINE.md)
- [Office ingestion](docs/OFFICE_INGESTION.md)
- [Translation pipeline](docs/TRANSLATION_PIPELINE.md)
- [Signing](docs/SIGNING.md)
- [Packaging](packaging/README.md)
