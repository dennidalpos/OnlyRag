# Architettura

OnlyRag è un'applicazione desktop Windows local-first composta da uno shell WPF, un'interfaccia utente React/Vite in WebView2 e un backend .NET in-process.

## Struttura dei Sorgenti

- [`src/OnlyRag.App`](../src/OnlyRag.App): Shell desktop WPF, hosting WebView2, avvio e chiusura dell'applicazione.
- [`src/OnlyRag.Web`](../src/OnlyRag.Web): Frontend React/Vite, tipi di bridge API, interfaccia di visualizzazione del Knowledge Graph (`GraphView`), test UI e test e2e Playwright.
- [`src/OnlyRag.Api`](../src/OnlyRag.Api): Backend Minimal API in-process, motore per Agenti Autonomi SOTA, endpoint dell'app, endpoint delle dipendenze, endpoint Cloud LLM, endpoint Graph, orchestrazione dei job, integrazione Ollama, gestione runtime Qdrant e mappatura errori per l'utente.
- [`src/OnlyRag.Core`](../src/OnlyRag.Core): Contratti condivisi, DTO delle impostazioni, risposte standard (`ApiResponse<T>`), DTO del grafo e modelli di richiesta.
- [`src/OnlyRag.Infrastructure`](../src/OnlyRag.Infrastructure): Storage SQLite schema v11, Knowledge Graph retrieval (`SqliteGraphRetrievalService`), Re-ranking Cross-Encoder ONNX (`OnnxCrossEncoderReRankerService` con fallback `HeuristicReRankerService`), Dual OCR engine (`OnnxDirectMlOcrEngine` nativo C# DirectML ONNX + `PaddleOcrEngine` bridge Python), client factory Cloud LLM (`CloudLlmClientFactory`), generazione immagini ONNX DirectML, conversione ed esportazione PDF via LibreOffice, motori di retrieval e adapter vettoriali Qdrant.
- [`src/OnlyRag.Worker`](../src/OnlyRag.Worker): Astrazioni per la coda locale dei job e gestione dello stato dei task in background.
- [`tests`](../tests): Test xUnit per i layer .NET e host backend Playwright per i test di contratto e2e del frontend.
- [`scripts`](../scripts): Automazione PowerShell 7 per bootstrap, gate di verifica, build, packaging, firma, brand asset, OCR e pulizia workspace.
- [`packaging`](../packaging): Script NSIS e payload/manifest del runtime Qdrant integrato.
- [`assets/brand`](../assets/brand): Asset grafici di marca generati, immagini di setup e social.

## Confini del Runtime

L'applicazione WPF avvia il backend in-process e ospita l'interfaccia React all'interno del controllo WebView2. Il frontend comunica con il backend tramite chiamate HTTP REST e hub ASP.NET Core SignalR in tempo reale (`ChatStreamHub`, `JobProgressHub`) per lo streaming dei token e l'avanzamento live dei job. Le build di debug possono utilizzare un server di sviluppo Vite tramite l'ambiente `ONLYRAG_WEB_DEV_SERVER`.

I carichi di lavoro LLM sono unificati sotto `Microsoft.Extensions.AI` (`IChatClient`, `IEmbeddingGenerator`), supportando provider locali (Ollama) e provider Cloud LLM (OpenAI, Anthropic, Groq, OpenRouter, DeepSeek, Google Gemini). Qdrant viene gestito e avviato localmente dall'applicazione. SQLite (gestito tramite EF Core 10 `OnlyRagDbContext` ed estensioni FTS5) rappresenta il sistema di record locale per documenti, chunk, nodi/archi del Knowledge Graph, impostazioni, job, chat, traduzioni, cache OCR, memorie episodiche dell'agente e metadati di indicizzazione.

## Dati e Processi

I dati utente risiedono in `%LOCALAPPDATA%\OnlyRag`. I file dell'applicazione installata risiedono in `%LOCALAPPDATA%\Programs\OnlyRag`.

L'ingestione di documenti a lungo termine, l'OCR, i calcoli degli embedding e la traduzione sono eseguiti come job streaming ad alte prestazioni (`StreamingDocumentIngestionPipeline` basata su `System.Threading.Channels`). Gli aggiornamenti in tempo reale vengono inviati tramite SignalR con fallback di polling REST. La chiusura confermata dell'app annulla i job locali attivi, salva il lavoro UI disponibile e arresta i processi backend.

## Modello delle Dipendenze

Lo sviluppo richiede .NET 10, npm da Node.js e PowerShell 7. I pacchetti installer per l'utente finale sono autotenuti per i componenti runtime .NET e includono il runtime Qdrant integrato. WebView2, Ollama, Python, NSIS e strumenti di firma sono prerequisiti esterni in base al workflow. LibreOffice è opzionale ed è utilizzato per l'esportazione PDF delle traduzioni.

Nessun codice nel repository memorizza segreti o credenziali. I file di certificato PFX per la firma devono rimanere all'esterno del repository.

