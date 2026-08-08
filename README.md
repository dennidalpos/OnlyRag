# OnlyRag

<p align="center">
  <img src=".github/assets/onlyrag-logo-horizontal.png" width="360" alt="OnlyRag">
</p>

[![CI](https://github.com/dennidalpos/OnlyRag/actions/workflows/ci.yml/badge.svg)](https://github.com/dennidalpos/OnlyRag/actions/workflows/ci.yml)

OnlyRag è un'applicazione desktop Windows per la gestione di una libreria documentale locale con ricerca guidata da Ollama e Cloud LLM, chat RAG a 6 stadi, OCR Dual-Engine, traduzione locale e generazione di immagini ONNX DirectML.

L'applicazione è 100% local-first. Documenti, indici, job, impostazioni, storico chat, cache OCR, log e profili WebView2 risiedono in `%LOCALAPPDATA%\OnlyRag`.

## Funzionalità Supportate

- Importazione di TXT, Markdown, CSV, PDF, DOCX, XLSX, PPTX, immagini e archivi (ZIP, TAR, 7Z).
- OCR per PDF scansionati ed immagini via **motore nativo C# DirectML ONNX** (zero dipendenze Python) o bridge opzionale Python PaddleOCR.
- Generazione di embedding con Ollama o Cloud LLM e salvataggio vettori in Qdrant locale.
- Ricerca ibrida 2 stadi su documenti selezionati tramite SQLite FTS5, Qdrant HNSW, **Knowledge Graph Traversal** e **Re-ranking ONNX Cross-Encoder** (`OnnxCrossEncoderReRankerService`).
- Esplorazione interattiva dei concetti e delle relazioni tramite il **Visualizzatore Knowledge Graph** (`/api/graph/*`).
- Chat sui documenti con verifica di confidenza **CRAG** e citazioni grounded `[Pag. X, Chunk Y]`.
- Motore per **Agenti Autonomi SOTA** (ciclo a 6 fasi, orchestrazione subagenti DAG, memoria episodica).
- Traduzione locale dei documenti indicizzati con esportazione in TXT, Markdown, HTML, DOCX o PDF.
- Generazione immagini ONNX DirectML locale (`lcm-sdxl-olive-onnx`) con editor canvas integrato.

## Prerequisiti

Sviluppo:

- Windows 10 versione 1809/build 17763 o più recente, oppure Windows 11.
- PowerShell 7 (`pwsh`).
- SDK .NET 10 (selezionato via `global.json`).
- Node.js `^20.19.0 || >=22.12.0` con npm.
- Microsoft Edge WebView2 Runtime.

## Avvio Rapido

Dalla radice del repository con PowerShell 7:

```powershell
pwsh .\scripts\Bootstrap-Prerequisites.ps1
pwsh .\scripts\Build-Web.ps1
dotnet run --project .\src\OnlyRag.App\OnlyRag.App.csproj --configuration Debug
```

## Mappa dei Comandi

| Attività | Comando |
|---|---|
| Setup dipendenze | `pwsh .\scripts\Bootstrap-Prerequisites.ps1` |
| Avvio applicazione desktop | `dotnet run --project .\src\OnlyRag.App\OnlyRag.App.csproj --configuration Debug` |
| Avvio Vite dev server | `Set-Location .\src\OnlyRag.Web; npm run dev` |
| Gate di verifica rapido | `pwsh .\scripts\Invoke-Gate.ps1 -Fast` |
| Gate di verifica completo | `pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release` |
| Compilazione UI web | `pwsh .\scripts\Build-Web.ps1` |
| Compilazione app desktop | `pwsh .\scripts\Build-App.ps1 -Configuration Release` |
| Compilazione installer non firmato | `pwsh .\scripts\Build-Installer.ps1 -Configuration Release` |
| Pulizia workspace | `pwsh .\scripts\Clean.ps1` |

## Documentazione

- [Indice Documentazione](docs/README.md)
- [Operazioni e Handoff](docs/OPERATIONS.md)
- [Architettura Systema](docs/ARCHITECTURE.md)
- [Flusso Applicazione](docs/APP_FLOW.md)
- [Inventario Script](scripts/README.md)
- [Motore Agenti Autonomi](docs/AGENT_ENGINE.md)
- [Pipeline RAG & Knowledge Graph](docs/RAG_PIPELINE.md)
- [Pipeline OCR Dual-Engine](docs/OCR_PIPELINE.md)
- [Generazione Immagini](docs/IMAGE_GENERATION.md)
- [Pipeline Traduzione](docs/TRANSLATION_PIPELINE.md)
- [Firma Digitale Authenticode](docs/SIGNING.md)
- [Packaging Installer NSIS](packaging/README.md)
- [Asset di Marca](docs/BRAND_ASSETS.md)
- [Tracker Operativo](PROJECT_STATUS.json)

