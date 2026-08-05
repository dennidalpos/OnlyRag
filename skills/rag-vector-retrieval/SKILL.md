---
name: rag-vector-retrieval
description: Specialized skill for local Retrieval-Augmented Generation (RAG 2.0) in OnlyRag. Covers Dual-Tier Parent-Child document parsing (PDF, DOCX, XLSX, PPTX, TXT, MD, CSV), SQLite FTS5 keyword indexing, Qdrant vector database storage, Query Transformation, Reciprocal Rank Fusion (RRF), Heuristic 2nd-stage re-ranking, CRAG evaluation, Ollama embeddings/LLM inference, and retrieval evaluation metrics.
---

# Local RAG & Vector Retrieval Skill (Next-Gen 2.0)

This skill provides guidelines and operational procedures for maintaining and optimizing the document ingestion, 2-stage vector retrieval, and LLM chat pipeline in OnlyRag.

## 1. Official Documentation Sources

- **SQLite FTS5 Extension**: [sqlite.org/fts5.html](https://www.sqlite.org/fts5.html)
- **Qdrant Vector Database**: [qdrant.tech/documentation](https://qdrant.tech/documentation/)
- **Ollama API Documentation**: [github.com/ollama/ollama/blob/main/docs/api.md](https://github.com/ollama/ollama/blob/main/docs/api.md)
- **ECMA-376 / ISO/IEC 29500 (OpenXML Standard)**: [ecma-international.org/publications-and-standards/standards/ecma-376](https://www.ecma-international.org/publications-and-standards/standards/ecma-376/)
- **PaddleOCR Documentation**: [github.com/PaddlePaddle/PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR)

## 2. Ingestion & Dual-Tier Chunking Pipeline

Supported formats:
- **Plain Text / Structured Data**: `.txt`, `.md`, `.csv`
- **PDF Documents**: Parsed with native text extraction; fallback to PaddleOCR runtime for scanned pages.
- **Office OpenXML Formats**: `.docx`, `.xlsx`, `.pptx` (parsed natively without requiring MS Office installation). Note: Legacy formats (`.doc`, `.xls`, `.ppt`) are explicitly not supported.
- **Archive ingestion**: `.zip`, `.tar`, `.7z` are validated and streamed by `ArchiveExtractionService`; TXT/MD/CSV, Office Open XML, and text-based PDF entries are indexed as pages of the archive document with entry-path provenance and per-entry checkpoints. The SQLite schema v6 table `archive_manifest_entries` stores one row per archive entry, including entry index/path, declared/actual sizes, SHA-256, status, error, and page/chunk counts. Repeated paths remain separate manifest rows and are marked `Duplicate` without being indexed twice. Unsupported entries are drained for limit accounting and marked `Skipped`; image-entry OCR is not implemented yet. The manifest is exposed through `GET /api/documents/{id}/archive-manifest`.

Dual-Tier Chunking strategy:
- **Child Chunks (~150 tokens)**: High-resolution chunks indexed in SQLite FTS5 and vectorized on Qdrant.
- **Parent Chunks (~1000 tokens / paragraph)**: Broad contextual chunks preserved in the current SQLite schema v6 (`chunks` with `parent_chunk_id`).

## 3. Next-Gen 6-Stage Retrieval Pipeline

1. **Query Transformation**: Multi-Query expansion, Sub-Query decomposition, or HyDE generation via `IQueryTransformationService` and Ollama LLM expander (`ILlmQueryExpander`).
2. **Coarse 1st-Stage Search**: Parallel retrieval from SQLite FTS5 and Qdrant HNSW vector index.
3. **Reciprocal Rank Fusion (RRF)**: Rank-based candidate fusion combining keyword and vector rankings.
4. **2nd-Stage Re-ranking**: Cross-scoring `(Query, Chunk)` via `IReRankerService` (`HeuristicReRankerService`).
5. **Parent-Child Resolution**: Resolving high-scoring child chunks to their rich parent chunk context using `ParentChildChunkResolver`.
6. **CRAG Evaluation & Grounded Citation**: Faithfulness confidence check via `CragEvaluator` and interactive `[Pag. X, Chunk Y]` citation badge formatting.
7. **Subagent Execution Engine**: Background research subagent execution managed via `ISubagentRunner`.


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

1. Always preserve document citation metadata (document id, file name, page number, chunk index, parent context) from ingestion through search response.
2. Ensure graceful degrading when external endpoints (Ollama) or local services (Qdrant) are unresponsive.
3. Protect database transactions when writing chunk embeddings; use batch inserts for vector updates to maintain responsiveness.
4. Treat PaddleOCR as an optional, private runtime. Its virtual environment must be built in a
   sibling staging directory and published only after a bridge health check; never repair packages
   in the live environment or surface raw Python/pip output to the UI.
5. Document chat must run `GroundingVerifier` before returning content. Reject uncited or unsupported
   factual paragraphs, preserve the verification result in `ChatResponse.Grounding`, and surface
   conflicts through `grounding_conflicting_evidence`.
