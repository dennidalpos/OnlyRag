# Architecture

OnlyRag is a Windows-only local desktop application.

## Project Layout

- `src\OnlyRag.App`: WPF .NET 10 host with WebView2.
- `src\OnlyRag.Web`: React/Vite TypeScript UI (React 19, Vite 7, TypeScript 5).
- `src\OnlyRag.Api`: in-process Minimal API backend, area-specific endpoint maps, handlers, services, Ollama client, and job handlers.
- `src\OnlyRag.Core`: shared contracts and local path conventions.
- `src\OnlyRag.Infrastructure`: SQLite storage, fresh schema initialization, ingestion, retrieval, OCR bridge.
- `src\OnlyRag.Worker`: persistent local job queue contracts and handler boundary.
- `tests\OnlyRag.Core.Tests`: unit tests for core contracts.
- `tests\OnlyRag.Api.Tests`: unit tests for API handlers and job queue logic.
- `tests\OnlyRag.Infrastructure.Tests`: unit tests for ingestion, OCR (fake engine), and storage.

## Runtime Model

The desktop app hosts the UI through WebView2 and starts the backend in-process during WPF startup. The backend is a Minimal API listener bound to loopback with a dynamic port selected at runtime. Windows service mode is not a supported entrypoint for the current desktop runtime. In Debug, the shell can load the Vite dev server from loopback; `ONLYRAG_WEB_DEV_SERVER` is accepted only for loopback `http` or `https` URLs without embedded credentials before the backend bridge is injected.

User data is stored under `%LOCALAPPDATA%\OnlyRag`, with the SQLite database at `%LOCALAPPDATA%\OnlyRag\data\onlyrag.db`. Imported source files are copied to `%LOCALAPPDATA%\OnlyRag\documents\originals`. Startup creates the current SQLite schema for a fresh database, migrates supported older OnlyRag schema versions after creating a pre-migration backup under `%LOCALAPPDATA%\OnlyRag\data\backups`, and rejects unsupported unversioned or newer schemas instead of guessing. Embeddings are stored persistently as SQLite BLOB data and searched through the `sqlite-vec` SQLite extension boundary. The unauthenticated liveness endpoint (`GET /health`) returns only a minimal healthy status; vector backend status, persistence, limits, and utilization are exposed through the authenticated diagnostics endpoint `GET /api/diagnostics/vector-health`.

The local job queue is persisted in SQLite and exposed through `GET /api/jobs`, `GET /api/jobs/{id}`, and pause/resume/cancel job actions. The app implements a controlled shutdown flow that handles active jobs and unsaved UI states (e.g., chat in sessionStorage, translation drafts in localStorage). WPF asks for confirmation when the UI or backend reports pending work; confirmed exit saves available UI state, calls `POST /api/app/prepare-shutdown`, cancels `Pending`, `Running`, and `Paused` jobs cooperatively, waits briefly for running handlers to unregister, and then stops the in-process backend. On backend startup, interrupted `Running` jobs are recovered to `Pending` so future handlers can resume from `checkpointJson`. The hosted worker defaults to one local worker slot and only executes jobs when a matching `ILocalJobHandler` is registered.

Local process launch is centralized behind `ILocalProcessLauncher`. Browser/download and Explorer
dispatch still require explicit UI confirmation at the API boundary. Long-running local commands run
without shell invocation, capture stdout/stderr concurrently, and terminate their process tree when
the caller cancels.

The document library is exposed through `GET /api/documents`, `GET /api/documents/{id}`, `POST /api/documents/import`, `DELETE /api/documents/{id}`, and `POST /api/documents/{id}/reindex`. Import computes SHA-256 while streaming the upload into local storage, deduplicates by hash, persists document metadata in SQLite, and enqueues a persistent `document-ingestion` job. Delete is physical: OnlyRag removes the SQLite record and deletes the copied file from `%LOCALAPPDATA%\OnlyRag\documents\originals`.

Chunk embedding is exposed through `POST /api/documents/{id}/embed` and `GET /api/documents/{id}/embedding-status`. The embedding job uses the configured Ollama embedding model, sends only chunk content to Ollama, checkpoints after each chunk or configured small batch, and upserts vectors by `(chunk_id, model)` plus chunk `content_hash`.

## Operations and CI

Operational setup, canonical commands, local run modes, troubleshooting, and packaging status are
centralized in [OPERATIONS.md](OPERATIONS.md).

CI is configured in `.github\workflows\ci.yml` and runs the repository checks on `windows-latest`
for pull requests.

## Chat and RAG

`POST /api/chat` accepts a message, optional `documentIds`, and optional `conversationId`. When `documentIds` is supplied the service runs hybrid retrieval first, injects retrieved chunk snippets as context, and returns a grounded answer with visible sources. Without `documentIds` the endpoint forwards the message directly to Ollama as a general conversation. Chat history is persisted in SQLite under `chat_history`.

`POST /api/search` searches only the supplied `documentIds` and returns ranked chunk snippets without generating an answer. Both endpoints use the same `HybridRetrievalService`.

## Packaging Boundary

Packaging details are maintained in [../packaging/README.md](../packaging/README.md). The current
repository workflow can produce an unsigned per-user Inno Setup installer, sign release candidates
through `scripts\Sign-Release.ps1` when a trusted code-signing certificate is available, and produce
installer verification evidence through `scripts\Test-InstallerRelease.ps1`.
