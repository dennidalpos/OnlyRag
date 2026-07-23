---
name: rag-vector-retrieval
description: Specialized skill for local Retrieval-Augmented Generation (RAG) in OnlyRag. Covers document parsing (PDF, DOCX, XLSX, PPTX, TXT, MD, CSV), SQLite FTS5 keyword indexing, Qdrant vector database storage, Ollama embeddings/LLM inference, and retrieval evaluation metrics.
---

# Local RAG & Vector Retrieval Skill

This skill provides guidelines and operational procedures for maintaining and optimizing the document ingestion, vector retrieval, and LLM chat pipeline in OnlyRag.

## 1. Official Documentation Sources

- **SQLite FTS5 Extension**: [sqlite.org/fts5.html](https://www.sqlite.org/fts5.html)
- **Qdrant Vector Database**: [qdrant.tech/documentation](https://qdrant.tech/documentation/)
- **Ollama API Documentation**: [github.com/ollama/ollama/blob/main/docs/api.md](https://github.com/ollama/ollama/blob/main/docs/api.md)
- **ECMA-376 / ISO/IEC 29500 (OpenXML Standard)**: [ecma-international.org/publications-and-standards/standards/ecma-376](https://www.ecma-international.org/publications-and-standards/standards/ecma-376/)
- **PaddleOCR Documentation**: [github.com/PaddlePaddle/PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR)

## 2. Ingestion & Document Processing Pipeline

Supported formats:
- **Plain Text / Structured Data**: `.txt`, `.md`, `.csv`
- **PDF Documents**: Parsed with native text extraction; fallback to PaddleOCR runtime for scanned pages.
- **Office OpenXML Formats**: `.docx`, `.xlsx`, `.pptx` (parsed natively without requiring MS Office installation). Note: Legacy formats (`.doc`, `.xls`, `.ppt`) are explicitly not supported.

Chunking strategy:
- Documents are split into semantic pages/chunks with token character bounds, overlap windows, and section metadata.
- Chunk records are saved in SQLite database under `%LOCALAPPDATA%\OnlyRag\onlyrag.db`.

## 3. Hybrid Search Strategy (SQLite FTS5 + Qdrant)

1. **Vector Retrieval**:
   - Chunks are embedded via Ollama (`/api/embeddings` or `/api/embed`).
   - Embeddings are stored in local Qdrant collections (`packaging/qdrant/manifest.json`).
   - Collections are grouped by embedding model and vector dimensionality.
2. **Keyword Fallback (SQLite FTS5)**:
   - Full-text search tokens are maintained in SQLite FTS5 tables (`documents_fts`).
   - If Qdrant is unavailable or unconfigured, retrieval automatically falls back to SQLite FTS5 keyword search and displays a vector-unavailable notice in the UI.
3. **Context Construction**:
   - Retrieved top-k chunks are deduplicated and formatted into context blocks with grounded source citations (`[Document: Page X]`).
   - Full documents are never sent as context; only relevant snippets are passed to Ollama chat endpoints.

## 4. Retrieval Evaluation & Metrics

Evaluate retrieval precision, recall, and context size using the repository evaluation script:

```powershell
pwsh .\scripts\Evaluate-Retrieval.ps1 -DatasetPath .\docs\retrieval-evaluation.sample.json
```

Evaluation metrics calculated:
- **Recall@K**: Proportion of target chunks present in the top-K retrieved results.
- **Mean Reciprocal Rank (MRR)**: Reciprocal rank of the first relevant chunk returned.
- **Context Character Count**: Average size of context payload passed to the LLM.

## 5. Operational Rules

1. Always preserve document citation metadata (document id, file name, page number, chunk index) from ingestion through search response.
2. Ensure graceful degrading when external endpoints (Ollama) or local services (Qdrant) are unresponsive.
3. Protect database transactions when writing chunk embeddings; use batch inserts for vector updates to maintain responsiveness.
