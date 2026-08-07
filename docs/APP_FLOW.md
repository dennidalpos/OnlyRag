# Application Flow

The editable source diagram is [`APP_FLOW.drawio`](APP_FLOW.drawio). This Markdown page is the
text fallback for review and handoff.

```mermaid
flowchart TD
    A["User launches OnlyRag.App.exe"] --> B["WPF shell starts"]
    B --> C["Windows/WebView2 prerequisites checked"]
    C --> D["%LOCALAPPDATA%\\OnlyRag paths prepared"]
    D --> E["SQLite schema initialized and persistent jobs recovered"]
    E --> F["In-process backend starts on dynamic loopback port"]
    F --> G["WebView2 loads React UI from Vite dev server or bundled static assets"]
    G --> H["Backend bridge injects base URL and session token"]
    H --> I["Initial polling: app, diagnostics, settings, dependencies, OCR languages, Qdrant, Ollama"]
    I --> J["User works in Chat, Documents, Jobs, Translation, Settings"]

    J --> K["Settings: configure Ollama, Cloud LLMs, Qdrant, OCR, PDF export, models, ingestion, performance, reset"]
    K --> L["External endpoints: Ollama, Cloud LLMs (OpenAI, Anthropic, Groq, OpenRouter, DeepSeek), Qdrant, PaddleOCR Python, LibreOffice"]

    J --> M["Documents: import files with OCR policy and document language"]
    M --> N["Validate upload limits, storage quota, file names, dedupe hash, local copy"]
    N --> O["Create document row and enqueue document-ingestion job"]
    O --> P["Worker extracts TXT/MD/CSV/OpenXML/PDF/image content"]
    P --> Q["Native C# DirectML ONNX OCR engine or optional PaddleOCR bridge with cache/retry/timeout"]
    Q --> R["Persist pages, chunks, graph nodes/edges, OCR status, preview/pipeline state in SQLite"]
    R --> S["If embedding model exists, enqueue document-embedding"]
    S --> T["Ollama/Cloud LLM embeds chunks; Qdrant stores vectors by model/vector shape"]

    J --> U["Chat/Search: user submits query with or without selected documents"]
    U --> V{"Document chat?"}
    V -->|yes| W["Hybrid retrieval: SQLite FTS5 + Graph Traversal + Ollama/Cloud LLM query embedding + Qdrant vector search"]
    W --> X["Prompt includes retrieved snippets and graph context"]
    V -->|no| Y["Direct chat prompt"]
    X --> Z["Ollama/Cloud LLM chat response"]
    Z --> AA["Persist chat turn and return answer, sources, notices"]

    J --> GV["Graph Visualizer: interactively query and explore concept graph networks (/api/graph/data, /api/graph/search)"]

    J --> AB["Translation: create translation for indexed document"]
    AB --> AC["Verify model/document, create page-based units, enqueue document-translation"]
    AC --> AD["Worker prompts Ollama, validates output, checkpoints units"]
    AD --> AE["User reviews/corrects units and exports TXT/MD/HTML/DOCX/PDF"]

    J --> AF["Jobs: poll queue, pause/resume/cancel/delete local jobs"]
    J --> AI["Coding: start or resume a persistent agent run"]
    AI --> AJ["SQLite records run snapshot, phase transitions, budgets, and conversation state"]
    AJ --> AK["PLAN → ACT → OBSERVE → VERIFY; failures go through RECOVER"]
    AK --> AL["FINALIZE → COMPLETED, or resume an interrupted non-terminal run"]
    J --> AG["Confirmed exit or reset request"]
    AG --> AH["Save available UI work, cancel active jobs, stop child processes/Qdrant, dispose backend"]
```

## Startup

The WPF app validates Windows and WebView2, prepares app directories, starts the backend,
initializes the current SQLite schema, recovers local job state, initializes WebView2, and loads either bundled static
web assets or the loopback Vite development server in Debug builds. Non-loopback or
credential-bearing `ONLYRAG_WEB_DEV_SERVER` URLs are ignored.

## Initial Setup

The UI exposes dependency status and setup actions for Ollama, Qdrant, OCR, and LibreOffice for
PDF export. The app opens official install pages for manual external installs where appropriate.
OCR provisioning uses repository runtime manifests and local Python when available. Qdrant
settings distinguish the bundled local runtime from trusted remote endpoints.

## Workflows

Document import validates local limits, creates local records, and enqueues persistent jobs.
Ingestion extracts text, optional OCR adds text for scanned/image content, embeddings are
generated through Ollama, and vector data is stored in Qdrant. Search and chat use selected
document scopes with hybrid SQLite FTS plus vector retrieval. Translation jobs generate editable
page-based units and exports.

## Agent runs

Coding-agent runs are durable SQLite records. The runtime, rather than the model prompt, enforces
the `Plan`, `Act`, `Observe`, `Verify`, `Recover`, and `Finalize` phases. It persists the LLM
conversation snapshot and phase transitions after each action cycle, applies tool-call, estimated-token,
and wall-clock budgets, and exposes `GET /api/agent/runs/{runId}` plus
`GET /api/agent/runs/resumable` for recovery after an application restart. A new streaming request can
pass `resumeRunId` to continue a non-terminal run.

Every new run also persists typed completion criteria and runtime-observed verification evidence.
`FINALIZE` and `COMPLETED` are blocked until every required criterion has a successful matching tool
result. Command criteria may require an exact `run_command`; when omitted, the default criterion
accepts only a successful build, test, lint, typecheck, or release-gate command. Model claims and
`reflect_step` output are never treated as completion evidence.

## Agent evaluation traces

Every run appends immutable events to `agent_run_trace_events`: goal decision, phase, model response
latency, tool result, observations, errors, token usage, evidence and terminal outcome. Events are
available from `GET /api/agent/runs/{runId}/trace`. The committed
[`agent-evaluation.dataset.json`](agent-evaluation.dataset.json) defines repeatable real development
tasks and expected limits for success, regressions, duration and step count.

## Shutdown

When local jobs or unsaved UI work exist, the app asks for confirmation. Confirmed exit saves
available work, cancels active local jobs, and shuts down the in-process backend.
