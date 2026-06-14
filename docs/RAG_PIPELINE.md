# RAG Pipeline

OnlyRag uses local document storage, SQLite keyword search, Qdrant vector search, and Ollama model
calls for retrieval-augmented chat.

## Ingestion

Supported import formats documented by the current app are TXT, Markdown, CSV, PDF, DOCX, XLSX,
PPTX, and image files. Binary Office formats such as `.doc`, `.xls`, and `.ppt` are not imported.

The ingestion layer lives under [`src/OnlyRag.Infrastructure/Ingestion`](../src/OnlyRag.Infrastructure/Ingestion).
Document records, chunks, pages, job state, and indexing metadata are stored in SQLite through
[`src/OnlyRag.Infrastructure/Storage`](../src/OnlyRag.Infrastructure/Storage).

## Embeddings And Vector Storage

Embedding jobs call the configured Ollama endpoint. Vectors are stored in Qdrant through
[`QdrantVectorStore`](../src/OnlyRag.Infrastructure/Vector/QdrantVectorStore.cs). Local Qdrant is
bundled from [`packaging/qdrant/manifest.json`](../packaging/qdrant/manifest.json) and prepared by
[`scripts/Download-Qdrant.ps1`](../scripts/Download-Qdrant.ps1) when needed.

Collections are separated by embedding model and vector shape. SQLite remains the system of
record for metadata; Qdrant is the vector index.

## Search And Chat

Retrieval combines SQLite FTS keyword signals and Qdrant vector results through the retrieval
services under [`src/OnlyRag.Infrastructure/Retrieval`](../src/OnlyRag.Infrastructure/Retrieval).
Chat sends retrieved snippets to Ollama and displays source snippets for grounded answers. It does
not send full source documents as the normal RAG context.

If query embeddings or Qdrant vector search are unavailable, retrieval continues with SQLite FTS
keyword results when possible and returns retrieval notices to the UI. Document chat only returns a
no-context answer when no selected document chunk can be retrieved.

## Operational Limits

- Model features require a reachable Ollama endpoint.
- Qdrant must be available for vector search. Keyword-only search/chat can still work when indexed
  chunks exist, but results include notices that vector retrieval is unavailable.
- Documents migrated from any older vector backend require Qdrant re-indexing before vector
  search/chat can use them.
- Remote Qdrant use should be explicitly trusted and protected as configured in Settings.

## Retrieval Evaluation

Use the local harness to track retrieval quality while tuning chunking, embeddings, keyword search,
or context limits:

```powershell
pwsh .\scripts\Evaluate-Retrieval.ps1 -DatasetPath .\docs\retrieval-evaluation.sample.json
```

Dataset cases define a representative query, selected document IDs, expected chunk IDs, and either
inline search results or a live backend target supplied with `-BackendBaseUrl` and `-SessionToken`.
The generated report records per-case returned chunks, recall@k, reciprocal rank, first relevant
rank, and context character count, plus summary recall@k, MRR, and average context size.
