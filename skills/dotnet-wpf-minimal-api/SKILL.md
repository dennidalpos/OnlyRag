---
name: dotnet-wpf-minimal-api
description: Technical skill for C# .NET 10 development in OnlyRag, covering WPF application hosting, Microsoft Edge WebView2 bridge, ASP.NET Core Minimal API endpoints, Dependency Injection, SQLite storage, and xUnit testing.
---

# .NET 10, WPF & Minimal API Backend Skill

This skill provides technical patterns and best practices for developing the C# .NET 10 backend components of OnlyRag.

## 1. Official Documentation Sources

- **Microsoft .NET 10 Documentation**: [learn.microsoft.com/dotnet](https://learn.microsoft.com/en-us/dotnet/)
- **WPF (Windows Presentation Foundation)**: [learn.microsoft.com/dotnet/desktop/wpf](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
- **ASP.NET Core Minimal APIs**: [learn.microsoft.com/aspnet/core/fundamentals/minimal-apis](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis)
- **Microsoft Edge WebView2**: [learn.microsoft.com/microsoft-edge/webview2](https://learn.microsoft.com/en-us/microsoft-edge/webview2/)
- **xUnit.net Testing**: [xunit.net/docs/getting-started/netcore/cmdline](https://xunit.net/docs/getting-started/netcore/cmdline)
- **Microsoft.Data.Sqlite**: [learn.microsoft.com/dotnet/standard/data/sqlite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/)

## 2. Architecture & Layering

OnlyRag enforces clear assembly boundaries:

| Assembly | Path | Purpose | Key Dependencies |
|---|---|---|---|
| `OnlyRag.App` | [`src/OnlyRag.App`](file:///d:/GITHUB/OnlyRag/src/OnlyRag.App) | WPF Desktop entrypoint, `MainWindow.xaml`, WebView2 initialization, process lifecycles | `Microsoft.Web.WebView2`, `OnlyRag.Api` |
| `OnlyRag.Api` | [`src/OnlyRag.Api`](file:///d:/GITHUB/OnlyRag/src/OnlyRag.Api) | In-process WebApplication, Minimal API route map, HTTP/bridge handlers, job endpoints | `OnlyRag.Core`, `OnlyRag.Infrastructure`, `OnlyRag.Worker` |
| `OnlyRag.Core` | [`src/OnlyRag.Core`](file:///d:/GITHUB/OnlyRag/src/OnlyRag.Core) | DTOs, contracts, options, standard response wrappers (`ApiResponse<T>`) | Standard Library only |
| `OnlyRag.Infrastructure` | [`src/OnlyRag.Infrastructure`](file:///d:/GITHUB/OnlyRag/src/OnlyRag.Infrastructure) | SQLite FTS5 repositories, Qdrant client vector store, Ollama HTTP client, ONNX DirectML provider, OCR bridge | `Microsoft.Data.Sqlite`, `Qdrant.Client`, `Microsoft.ML.OnnxRuntime.DirectML` |
| `OnlyRag.Worker` | [`src/OnlyRag.Worker`](file:///d:/GITHUB/OnlyRag/src/OnlyRag.Worker) | Local background job queue, channel-based queue execution, job status tracking | `OnlyRag.Core` |

## 3. Key Patterns & Conventions

### WPF + In-Process Minimal API Startup
- The WPF application (`App.xaml.cs`) boots an ASP.NET Core `WebApplication` on a dynamically selected loopback port and exposes the settings through the WebView2 startup bridge.
- WebView2 is initialized via `EnsureCoreWebView2Async` pointing to either the local built dist (`src/OnlyRag.Web/dist`) or `ONLYRAG_WEB_DEV_SERVER`.

### Clean Error Handling & Response Contracts
- Endpoint contracts are defined by the records in `OnlyRag.Core` and the feature-specific endpoint mappings. Map expected domain failures to the existing user-facing error model; never expose unhandled exception stack traces to the frontend UI.

### SQLite Storage Patterns
- SQLite connection and storage paths are resolved by the application storage services under `%LOCALAPPDATA%\OnlyRag`; do not hard-code a database path in a new service.
- WAL (Write-Ahead Logging) mode is enabled (`PRAGMA journal_mode=WAL;`) for concurrent read/write throughput during ingestion.
- FTS5 virtual tables (`documents_fts`) store tokenized content for fast keyword retrieval fallback.

### Asynchronous Programming
- Use `async` / `await` for all IO operations (file access, HTTP requests to Ollama/Qdrant, database queries).
- Use `CancellationToken` throughout service signatures to facilitate clean shutdown on app exit.

## 4. Verification & Testing

Run unit and integration tests using `dotnet test`:

```powershell
# Run all solution tests in Release configuration
dotnet test .\OnlyRag.sln --configuration Release

# Run specific project tests with filter
dotnet test .\tests\OnlyRag.Infrastructure.Tests\OnlyRag.Infrastructure.Tests.csproj --filter "FullyQualifiedName~Storage"
```

## 5. Coding Rules

1. Maintain nullable reference types (`<Nullable>enable</Nullable>`) enabled in all `.csproj` files.
2. Use modern C# 13 features where appropriate (primary constructors, pattern matching, file-scoped namespaces).
3. Do not place UI or WPF dependencies inside `OnlyRag.Core`, `OnlyRag.Api`, or `OnlyRag.Infrastructure`.
