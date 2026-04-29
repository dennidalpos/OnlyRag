# RAG Pipeline

OnlyRag implements local document ingestion, chunk embeddings, hybrid retrieval, and
document-grounded chat answer generation.

Implemented stages:

1. Import document into local storage.
2. Extract text and metadata. Legacy or mixed Office files use optional LibreOffice headless
   conversion to PDF before entering the existing PDF path.
3. Chunk document content.
4. Generate embeddings through local or LAN Ollama integration.
5. Store chunks and vectors in SQLite BLOB format.
6. Retrieve relevant chunks from selected documents through hybrid keyword/vector search.
7. Generate answers with cited local context via `POST /api/chat`.

## Ingestion Settings

Chunking is configured in Settings and persisted under `%LOCALAPPDATA%\OnlyRag` with these keys:

- `ingestion.chunkSizeTokens`: default `800`, clamped to `100..4000`.
- `ingestion.overlapTokens`: default `120`, clamped to `0..min(1000, chunkSize / 2)`.

The settings UI can show model-based suggestions from the selected embedding model context
window. Suggestions are visual only and do not change saved values automatically.

## Embeddings

`POST /api/documents/{id}/embed` enqueues a persistent `document-embedding` job for an
already chunked document. The job reads only stored chunks, never the full original document,
and sends them to Ollama one chunk at a time by default. The embedding model, request timeout,
and embedding batch size are configured in Settings. Batch size is capped at 8 chunks to keep
the local workflow usable on slower Windows PCs.

Embeddings are saved in SQLite with `chunk_id`, `model`, `dimensions`, `content_hash`,
`vector_blob`, and `created_at_utc`. A chunk is regenerated when the selected model has no
stored vector or when the stored `content_hash` no longer matches the chunk.

`GET /api/documents/{id}/embedding-status` reports the configured model, chunk count,
embedded chunk count, progress, and the active embedding job when present.

## Vector Search Backend

`IVectorSearchService` is backed by the `sqlite-vec` SQLite extension through the `sqlite-vec`
NuGet package. Vectors stay persisted as SQLite BLOB values and are searched by SQL through
`vec_distance_cosine` inside the selected document scope. The native `vec0.dll` asset is copied by
the infrastructure project for Windows build and packaging.

If query embedding or vector search is unavailable for a request, keyword retrieval still runs and
the response reports vector search as unavailable for that query.

## Hybrid Retrieval

`POST /api/search` searches only the `documentIds` supplied in the request. The service sends
only the user query to Ollama to generate a query embedding; it never sends full documents to
the model. Candidate chunks are retrieved through:

- SQLite FTS5 keyword search when the `chunks_fts` virtual table is available.
- Document-scoped SQLite `LIKE` fallback when FTS5 is unavailable or the FTS query cannot be
  executed.
- `IVectorSearchService` over stored chunk embeddings for the configured embedding model.

The retrieval service merges keyword and vector candidates, deduplicates by `chunk_id`,
normalizes scores, limits returned snippets by a configurable maximum context budget, and
returns document name, page range, chunk id, snippet, and score. If query embedding or vector
search is unavailable, the endpoint still returns keyword results and reports the vector
backend as unavailable for that query.

## Chat and Document-Grounded Answers

`POST /api/chat` accepts a message, optional `documentIds`, and optional `conversationId`.

- **Without `documentIds`**: message is forwarded directly to Ollama as a general conversation.
- **With `documentIds`**: hybrid retrieval runs first; retrieved chunk snippets are injected as
  context into the Ollama prompt. The response includes visible sources (document name, page,
  chunk id, snippet) alongside the generated answer.

The chat model and endpoint are configured in Settings > Ollama. Chat history is persisted in
SQLite (`chat_history` table) on the backend, and active chat state (conversation ID, messages,
selected documents) is preserved in `sessionStorage` on the frontend to survive section navigation.
The service never sends full document text to Ollama, only retrieved snippets within the
configured context budget.

## Known Limits

- Vector search depends on the Windows native `vec0.dll` copied from the `sqlite-vec` NuGet
  package. Build and packaging validation must keep this file in the published payload.
- Embedding batch size is capped at 8 chunks to keep the workflow usable on slower Windows PCs.
- FTS5 availability depends on the SQLite build distributed with `Microsoft.Data.Sqlite`; the
  LIKE fallback is used automatically when FTS5 is unavailable.
