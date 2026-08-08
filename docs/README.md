# Documentazione OnlyRag

Il file [README](../README.md) nella radice è la guida rapida e la mappa dei comandi. Questa directory contiene la documentazione tecnica ed operativa corrente di OnlyRag.

## Operazioni

- [Operazioni e handoff](OPERATIONS.md): Prerequisiti Windows, configurazione, sviluppo/avvio, gate di verifica, packaging, firma e risoluzione problemi.
- [Script](../scripts/README.md): Inventario completo degli script PowerShell 7 e flussi di comandi canonici.
- [Firma Digitale](SIGNING.md): Gestione dei certificati e comandi di firma dei rilascio Authenticode.
- [Packaging](../packaging/README.md): Input, output, comportamento e verifica dell'installer NSIS.

## Architettura e Flusso

- [Architettura](ARCHITECTURE.md): Struttura dei sorgenti, confini del runtime, storage SQLite/Qdrant, dipendenze e superfici di test.
- [Flusso dell'applicazione](APP_FLOW.md): Avvio desktop shell WPF, bridge backend, flusso dei job, chiusura e diagramma draw.io.

## Pipeline delle Funzionalità

- [Motore Agenti Autonomi](AGENT_ENGINE.md): Architettura del motore agenti autonomi SOTA (6 fasi), orchestratore subagenti DAG, sistema di memoria episodica ed endpoint API.
- [Pipeline RAG & Knowledge Graph](RAG_PIPELINE.md): Ingestione (OpenXML, PDF, archivi, TXT, CSV), chunking Parent-Child, Knowledge Graph traversal (`SqliteGraphRetrievalService`), Re-ranking ONNX Cross-Encoder (`OnnxCrossEncoderReRankerService`), indicizzazione Qdrant e grounding chat.
- [Pipeline OCR](OCR_PIPELINE.md): Architettura Dual-Engine OCR (Motore C# nativo DirectML ONNX `OnnxDirectMlOcrEngine` + Bridge Python PaddleOCR `PaddleOcrEngine`).
- [Generazione Immagini](IMAGE_GENERATION.md): Catalogo modelli locale ONNX DirectML/CPU (`lcm-sdxl-olive-onnx`), consenso download, verifica SHA256, editor canvas e verifiche di rilascio.
- [Pipeline di Traduzione](TRANSLATION_PIPELINE.md): Traduzione a unità per pagina con Ollama, validazione prompt ed esportazione multi-formato (TXT, MD, HTML, DOCX, PDF via LibreOffice).

## Asset

- [Asset di Marca](BRAND_ASSETS.md): Posizione degli asset generati e comandi di rigenerazione.

## File Tracciati

- [Tracker Operativo](../PROJECT_STATUS.json)
- [File Soluzione Visual Studio](../OnlyRag.sln)
- [Workflow CI GitHub Actions](../.github/workflows/ci.yml)
- [Workflow Catalogo OCR](../.github/workflows/ocr-runtime-catalog.yml)
- [Manifest Runtime Qdrant](../packaging/qdrant/manifest.json)
- [Manifest Runtime OCR](../scripts/ocr/runtime-manifest.json)

