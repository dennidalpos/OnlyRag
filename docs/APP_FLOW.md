# Application Flow

The editable source diagram is [`APP_FLOW.drawio`](APP_FLOW.drawio). This Markdown page is the
text fallback for review and handoff.

```mermaid
flowchart TD
    A["User launches OnlyRag.App.exe"] --> B["WPF shell starts"]
    B --> C["Windows/WebView2 prerequisites checked"]
    C --> D["%LOCALAPPDATA%\\OnlyRag paths prepared"]
    D --> E["SQLite migrated and persistent jobs recovered"]
    E --> F["In-process backend starts on dynamic loopback port"]
    F --> G["WebView2 loads React UI from Vite dev server or bundled static assets"]
    G --> H["Backend bridge injects base URL and session token"]
    H --> I["Initial polling: app, diagnostics, settings, dependencies, OCR languages, Qdrant, Ollama"]
    I --> J["User works in Chat, Documents, Jobs, Translation, Settings"]

    J --> K["Settings: configure Ollama, Qdrant, OCR, Office, models, ingestion, performance, reset"]
    K --> L["External tools: Ollama, Qdrant, PaddleOCR Python, LibreOffice, official download pages"]

    J --> M["Documents: import files with OCR policy and document language"]
    M --> N["Validate upload limits, storage quota, file names, dedupe hash, local copy"]
    N --> O["Create document row and enqueue document-ingestion job"]
    O --> P["Worker extracts TXT/MD/CSV/OpenXML/PDF/image content"]
    P --> Q["Optional LibreOffice conversion and optional PaddleOCR with cache/retry/timeout"]
    Q --> R["Persist pages, chunks, OCR status, preview/pipeline state in SQLite"]
    R --> S["If embedding model exists, enqueue document-embedding"]
    S --> T["Ollama embeds chunks; Qdrant stores vectors by model/vector shape"]

    J --> U["Chat/Search: user submits query with or without selected documents"]
    U --> V{"Document chat?"}
    V -->|yes| W["Hybrid retrieval: SQLite FTS + Ollama query embedding + Qdrant vector search"]
    W --> X["Prompt includes retrieved snippets only"]
    V -->|no| Y["Direct chat prompt"]
    X --> Z["Ollama chat response"]
    Y --> Z
    Z --> AA["Persist chat turn and return answer, sources, notices"]

    J --> AB["Translation: create translation for indexed document"]
    AB --> AC["Verify model/document, create page-based units, enqueue document-translation"]
    AC --> AD["Worker prompts Ollama, validates output, checkpoints units"]
    AD --> AE["User reviews/corrects units and exports TXT/MD/HTML/DOCX/PDF"]

    J --> AF["Jobs: poll queue, pause/resume/cancel/delete local jobs"]
    J --> AG["Confirmed exit or reset request"]
    AG --> AH["Save available UI work, cancel active jobs, stop child processes/Qdrant, dispose backend"]
```

## Startup

The WPF app validates Windows and WebView2, prepares app directories, starts the backend,
migrates SQLite, recovers local job state, initializes WebView2, and loads either bundled static
web assets or the loopback Vite development server in Debug builds. Non-loopback or
credential-bearing `ONLYRAG_WEB_DEV_SERVER` URLs are ignored.

## Initial Setup

The UI exposes dependency status and setup actions for Ollama, Qdrant, OCR, and LibreOffice. The
app opens official install pages for manual external installs where appropriate. OCR provisioning
uses repository runtime manifests and local Python when available. Qdrant settings distinguish
the bundled local runtime from trusted remote endpoints.

## Workflows

Document import validates local limits, creates local records, and enqueues persistent jobs.
Ingestion extracts text, optional OCR adds text for scanned/image content, embeddings are
generated through Ollama, and vector data is stored in Qdrant. Search and chat use selected
document scopes with hybrid SQLite FTS plus vector retrieval. Translation jobs generate editable
page-based units and exports.

## Shutdown

When local jobs or unsaved UI work exist, the app asks for confirmation. Confirmed exit saves
available work, cancels active local jobs, and shuts down the in-process backend.
