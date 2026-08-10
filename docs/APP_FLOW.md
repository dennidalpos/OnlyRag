# Flusso dell'Applicazione

Il diagramma sorgente modificabile è [`APP_FLOW.drawio`](APP_FLOW.drawio). Questa pagina Markdown è il riferimento testuale completo.

```mermaid
flowchart TD
    A["Lancio dell'applicazione OnlyRag.App.exe"] --> B["Avvio dello shell WPF"]
    B --> C["Verifica prerequisiti Windows / WebView2"]
    C --> D["Inizializzazione percorsi in %LOCALAPPDATA%\\OnlyRag"]
    D --> E["Inizializzazione schema SQLite v11 e recupero job persistenti"]
    E --> F["Avvio backend Minimal API in-process su porta loopback dinamica"]
    F --> G["WebView2 carica l'interfaccia React dal dev server Vite o dagli asset statici integrati"]
    G --> H["Il bridge backend inietta l'URL base e il token di sessione"]
    H --> I["Polling iniziale: stato app, diagnostica, impostazioni, dipendenze, OCR, Qdrant, Ollama"]
    I --> J["Operazioni utente: Chat, Documenti, Job, Traduzione, Agenti, Generazione Immagini, Impostazioni"]

    J --> K["Impostazioni: configurazione Ollama, Cloud LLM, Qdrant, OCR, esportazione PDF, modelli, ingestione, reset"]
    K --> L["Dipendenze locali o configurabili: Ollama, Cloud LLM (Azure OpenAI, OpenAI, Anthropic, Google Gemini), Qdrant, Python PaddleOCR, LibreOffice"]

    J --> M["Documenti: importazione file con policy OCR e lingua del documento"]
    M --> N["Convalida limiti upload, quota storage, nomi file, deduplicazione hash, copia locale"]
    N --> O["Creazione record documento ed enqueue del job document-ingestion"]
    O --> P["Worker estrae contenuti TXT/MD/CSV/OpenXML/PDF/archivi/immagini"]
    P --> Q["Engine OCR nativo DirectML ONNX o bridge Python PaddleOCR opzionale con cache/retry"]
    Q --> R["Salvataggio pagine, chunk Parent-Child, nodi/archi del grafo, stato OCR in SQLite"]
    R --> S["Se esiste il modello di embedding, enqueue di document-embedding"]
    S --> T["Ollama/Cloud LLM genera gli embedding; Qdrant salva i vettori per modello e dimensione"]

    J --> U["Chat/Search: l'utente invia una query con o senza documenti selezionati"]
    U --> V{"Chat sui documenti?"}
    V -->|sì| W["Retrieval Ibrido 6 Stadi: SQLite FTS5 + Traversal Grafo + Re-ranking ONNX Cross-Encoder + Qdrant Vector Search"]
    W --> X["Il prompt sintetizza gli snippet recuperati e il contesto di grafo"]
    V -->|no| Y["Prompt diretto di chat"]
    X --> Z["Risposta chat da Ollama/Cloud LLM con verifica GroundingVerifier"]
    Z --> AA["Salvataggio turno di chat e restituzione risposta, fonti e citazioni [Pag. X, Chunk Y]"]

    J --> GV["Visualizzatore Grafo: esplorazione interattiva della rete di concetti (/api/graph/data, /api/graph/search)"]

    J --> AB["Traduzione: creazione traduzione per un documento indicizzato"]
    AB --> AC["Verifica modello/documento, creazione unità per pagina, enqueue document-translation"]
    AC --> AD["Il worker interroga Ollama, valida l'output e salva i checkpoint delle unità"]
    AD --> AE["L'utente esamina/corregge le unità ed esporta in TXT/MD/HTML/DOCX/PDF"]

    J --> AF["Job: polling della coda, pausa/ripresa/annullamento/eliminazione job locali"]
    J --> AI["Agenti: avvio o ripresa di un run agente autonomo (6 fasi)"]
    AI --> AJ["SQLite registra lo snapshot del run, le transizioni di fase, i budget e lo stato della conversazione"]
    AJ --> AK["PLAN → ACT → OBSERVE → VERIFY; i fallimenti passano da RECOVER"]
    AK --> AL["FINALIZE → COMPLETED, o ripresa di un run non terminale interrotto"]
    J --> AG["Uscita o reset confermato dell'utente"]
    AG --> AH["Salvataggio lavoro UI disponibile, annullamento job attivi, arresto processi figli/Qdrant, chiusura backend"]
```

## Avvio

L'applicazione WPF convalida l'ambiente Windows e WebView2, prepara le directory locali, avvia il backend in-process su una porta dinamica, inizializza il database SQLite tramite EF Core 10 (`OnlyRagDbContext` / `LocalSqliteSchemaInitializer`), ripristina lo stato dei job, inizializza gli hub SignalR (`ChatStreamHub`, `JobProgressHub`) e carica l'interfaccia React in WebView2.

## Workflow

- **Importazione Documenti**: Convalida limiti locali, estrazione testo nativa (OpenXML/PDF/TXT), OCR automatico per immagini/PDF scansionati, generazione embedding via Ollama e salvataggio vettori su Qdrant.
- **Ricerca e Chat RAG**: Pipeline a 6 stadi basata su SQLite FTS5, Qdrant HNSW, re-ranking ONNX Cross-Encoder (`OnnxCrossEncoderReRankerService`), Knowledge Graph Traversal e verifica runtime `GroundingVerifier`.
- **Agenti Autonomi**: Ciclo a 6 fasi (`Plan` → `Act` → `Observe` → `Verify` → `Recover` → `Finalize`) guidato da `AgentLoopEngine`, memoria episodica e verifiche empiriche dei tool.
- **Chiusura**: Annullo sicuro dei job attivi, salvataggio dello stato utente e arresto controllato dei processi figli.
