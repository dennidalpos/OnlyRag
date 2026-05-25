# OnlyRag Documentation

This directory contains technical and operational notes for the current repository state.
The root [README](../README.md) remains the quick start; these pages expand the setup,
pipeline, packaging, and handoff details without acting as a changelog.

## Start Here

- [Operations and handoff](OPERATIONS.md): fresh-install setup, verification, release handoff,
  troubleshooting, and current blockers.
- [Architecture](ARCHITECTURE.md): source layout, runtime boundaries, storage, dependencies,
  and test surfaces.
- [Application flow](APP_FLOW.md): desktop startup, backend bridge, job flow, shutdown, and the
  editable draw.io diagram.

## Pipelines

- [RAG pipeline](RAG_PIPELINE.md): ingestion, embeddings, Qdrant indexing, hybrid search, and
  chat grounding.
- [OCR pipeline](OCR_PIPELINE.md): PaddleOCR bridge, runtime manifest, CPU/GPU selection, and
  operational limits.
- [Office ingestion](OFFICE_INGESTION.md): native OpenXML extraction and optional LibreOffice
  conversion for legacy Office files.
- [Translation pipeline](TRANSLATION_PIPELINE.md): page-based translation jobs, editing, and
  export formats.

## Release And Assets

- [Signing](SIGNING.md): signing certificate handling and release signing commands.
- [Brand assets](BRAND_ASSETS.md): generated asset locations and regeneration command.
- [Packaging](../packaging/README.md): installer payload, Inno Setup script, outputs, and
  verification evidence.
- [Scripts](../scripts/README.md): repository script inventory.

## Related Tracked Files

- [Operational tracker](../PROJECT_STATUS.json)
- [Solution file](../OnlyRag.sln)
- [CI workflow](../.github/workflows/ci.yml)
- [OCR catalog workflow](../.github/workflows/ocr-runtime-catalog.yml)
- [Qdrant runtime manifest](../packaging/qdrant/manifest.json)
- [OCR runtime manifest](../scripts/ocr/runtime-manifest.json)
