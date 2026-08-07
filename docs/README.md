# OnlyRag Documentation

The root [README](../README.md) is the quick start and command map. This directory contains
current technical and operational documentation. Do not use these pages as changelogs.

## Operations

- [Operations and handoff](OPERATIONS.md): Windows prerequisites, setup, dev/start, readiness
  gates, packaging, signing handoff, runtime paths, environment variables, and troubleshooting.
- [Scripts](../scripts/README.md): public PowerShell script inventory and canonical command flows.
- [Signing](SIGNING.md): certificate handling and release signing commands.
- [Packaging](../packaging/README.md): installer inputs, outputs, behavior, and release evidence.

## Architecture And Flow

- [Architecture](ARCHITECTURE.md): source layout, runtime boundaries, storage, dependencies, and
  test surfaces.
- [Application flow](APP_FLOW.md): desktop startup, backend bridge, job flow, shutdown, and the
  editable draw.io diagram.

## Feature Pipelines

- [Agent engine](AGENT_ENGINE.md): autonomous agent engine architecture, phase machine, subagent DAG orchestrator, memory system, and endpoints.
- [RAG & Knowledge Graph pipeline](RAG_PIPELINE.md): ingestion (OpenXML, PDF, archive, TXT, CSV), dual-tier chunking, Knowledge Graph retrieval (`SqliteGraphRetrievalService`), Qdrant indexing, hybrid search, and chat grounding.
- [OCR pipeline](OCR_PIPELINE.md): Dual-engine OCR architecture (Native C# DirectML ONNX engine `OnnxDirectMlOcrEngine` + Python PaddleOCR bridge `PaddleOcrEngine`).
- [Image generation](IMAGE_GENERATION.md): integrated local model catalog, required-file
  downloads, verification, GPU/CPU runtime behavior, toolbar editing workflow, and release checks.
- [Translation pipeline](TRANSLATION_PIPELINE.md): page-based translation jobs, editing, and
  export formats.

## Assets

- [Brand assets](BRAND_ASSETS.md): generated asset locations and regeneration command.

## Tracked Files

- [Operational tracker](../PROJECT_STATUS.json)
- [Solution file](../OnlyRag.sln)
- [CI workflow](../.github/workflows/ci.yml)
- [OCR catalog workflow](../.github/workflows/ocr-runtime-catalog.yml)
- [Qdrant runtime manifest](../packaging/qdrant/manifest.json)
- [OCR runtime manifest](../scripts/ocr/runtime-manifest.json)
