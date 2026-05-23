# Technical Documentation

This directory contains the technical documentation for OnlyRag. Keep the root `README.md`
product-facing and concise; keep operational and implementation details here.

## Canonical References

- [Operations](OPERATIONS.md): Windows setup, canonical commands, build/test/run flows, local
  data, troubleshooting, packaging status, and `PROJECT_STATUS.json` conventions.
- [Architecture](ARCHITECTURE.md): stack, project layout, runtime model, backend boundaries, and
  core service responsibilities.
- [Packaging](../packaging/README.md): Inno Setup model, installer contents, prerequisites, and
  pre-release checks.
- [Brand assets](BRAND_ASSETS.md): logo, favicon, installer, social/media assets, naming, and
  regeneration procedure.
- [Signing](SIGNING.md): certificate placement, signing script pipeline, and release verification
  steps.

## Pipeline Notes

- [Application flow](APP_FLOW.md): current WPF, WebView2, backend, persistence, job, and shutdown
  flow reconstructed from the implemented code.
- [Application flow diagram](APP_FLOW.drawio): editable draw.io diagram with runtime architecture
  and main product flows.
- [RAG pipeline](RAG_PIPELINE.md): ingestion, embeddings, sqlite-vec retrieval, chat behavior, and
  vector-search runtime requirements.
- [OCR pipeline](OCR_PIPELINE.md): scanned PDF/image OCR flow, cache, bridge, and prerequisites.
- [Office ingestion](OFFICE_INGESTION.md): Open XML extraction and optional LibreOffice conversion.
- [Translation pipeline](TRANSLATION_PIPELINE.md): translation jobs, correction flow, and export
  formats.

## Project Status

`PROJECT_STATUS.json` at the repository root is the authoritative backlog and project-state file
for residual, blocked, or planned work. Do not create parallel task lists in documentation files.
