# RAG Pipeline

OnlyRag uses local document storage, SQLite keyword search, Qdrant vector search, and Ollama model
calls for retrieval-augmented chat.

## Ingestion

Supported import formats documented by the current app are TXT, Markdown, CSV, PDF, DOCX, XLSX,
PPTX, and image files. Legacy `.doc`, `.xls`, and `.ppt` files require optional LibreOffice
conversion before extraction.

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

## Operational Limits

- Model features require a reachable Ollama endpoint.
- Qdrant must be available for vector search and grounded document chat.
- Documents migrated from any older vector backend require Qdrant re-indexing before vector
  search/chat can use them.
- Remote Qdrant use should be explicitly trusted and protected as configured in Settings.
