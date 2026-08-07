# Architecture

OnlyRag is a local-first Windows desktop application with a WPF shell, a React/Vite WebView UI, and an in-process .NET backend.

## Source Layout

- [`src/OnlyRag.App`](../src/OnlyRag.App): WPF desktop shell, WebView2 hosting, app startup and shutdown.
- [`src/OnlyRag.Web`](../src/OnlyRag.Web): React/Vite frontend, API bridge types, Knowledge Graph Visualization UI (`GraphView`), UI tests, and Playwright tests.
- [`src/OnlyRag.Api`](../src/OnlyRag.Api): in-process Minimal API backend, Autonomous Agent Engine SOTA, app endpoints, dependency endpoints, Cloud LLM endpoints, Graph endpoints, job orchestration, Ollama integration, Qdrant runtime management, and user-facing error mapping.
- [`src/OnlyRag.Core`](../src/OnlyRag.Core): shared contracts, settings DTOs, responses, graph DTOs, and request models.
- [`src/OnlyRag.Infrastructure`](../src/OnlyRag.Infrastructure): SQLite storage, Knowledge Graph retrieval (`SqliteGraphRetrievalService`), Dual OCR engine (Native DirectML ONNX `OnnxDirectMlOcrEngine` + Python PaddleOCR bridge `PaddleOcrEngine`), Cloud LLM client factory (`CloudLlmClientFactory`), ONNX DirectML image generation, PDF export conversion, retrieval, and Qdrant vector store adapters.
- [`src/OnlyRag.Worker`](../src/OnlyRag.Worker): local job queue abstractions and job state.
- [`tests`](../tests): xUnit tests for .NET layers and a Playwright backend host for frontend e2e contract tests.
- [`scripts`](../scripts): PowerShell automation for bootstrap, gates, build, packaging, signing, assets, OCR, and cleanup.
- [`packaging`](../packaging): NSIS script and bundled Qdrant runtime manifest/payload.
- [`assets/brand`](../assets/brand): generated source brand assets and setup/social imagery.

## Runtime Boundaries

The WPF app starts the in-process backend and hosts the React UI inside WebView2. The frontend talks to the backend through the app bridge rather than exposing a public remote API. Debug builds can use a loopback Vite development server when `ONLYRAG_WEB_DEV_SERVER` is set to a loopback `http` or `https` URL.

Ollama and Cloud LLM providers (OpenAI, Anthropic, Groq, OpenRouter, DeepSeek) execute model workloads. Ollama can run locally or on a trusted LAN endpoint. Qdrant can be bundled and managed locally by the app, with remote Qdrant configuration guarded by trust/TLS checks in settings. SQLite is the local system of record for documents, chunks, Knowledge Graph nodes/edges, settings, jobs, chat, translations, OCR cache, agent episodic memories, subagent report cache, and indexing metadata.

## Data And Processes

User data lives under `%LOCALAPPDATA%\OnlyRag`. Installed app files live under `%LOCALAPPDATA%\Programs\OnlyRag` after installer installation.

Long-running ingestion, OCR, embedding, and translation work is represented as local jobs. The UI polls job state and shows progress. Confirmed app exit cancels active local jobs, persists available UI work, and stops backend processes.

## Dependency Model

Development uses .NET 10, npm from Node.js, and PowerShell 7. End-user installer payloads are self-contained for .NET runtime components and include the bundled Qdrant runtime. WebView2, Ollama, Python, NSIS, and signing tools are external prerequisites depending on workflow. LibreOffice is optional and used only for translation PDF export.

No repository code should store secrets. Signing PFX files must stay outside the repository.
