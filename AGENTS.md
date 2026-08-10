# AGENTS.md — OnlyRag

## 1. Priorità e metodo di lavoro

- La richiesta dell'utente ha precedenza sulle regole del repository.
- Prima di modificare il codice, controlla `git status --short` e conserva le modifiche preesistenti non correlate.
- Segui il flusso reale dell'applicazione e riusa i servizi, i contratti e i pattern già presenti. Non introdurre framework, dipendenze o livelli architetturali non richiesti.
- Mantieni gli entrypoint sottili: la logica di dominio appartiene ai servizi nei moduli appropriati.
- Non eseguire comandi distruttivi sul repository (`git reset --hard`, `git clean`, checkout distruttivi, force-push o riscrittura della storia). Non cancellare dati utente in `%LOCALAPPDATA%\OnlyRag`.

## 2. Architettura reale e stack

OnlyRag è un'applicazione desktop Windows local-first:

- `src/OnlyRag.App`: shell WPF `net10.0-windows`, runtime `win-x64`, hosting WebView2. Avvia e arresta il backend in-process, inietta `window.__ONLYRAG_BACKEND__` e carica `src/OnlyRag.Web/dist`.
- `src/OnlyRag.Api`: backend ASP.NET Core Minimal API `net10.0` in-process. Le route sono divise in file `InProcessBackend.*.cs`, registrate da `InProcessBackend.EndpointMapping.cs`; SignalR gestisce streaming e notifiche job, REST gestisce stato e fallback.
- `src/OnlyRag.Core`: contratti condivisi, record DTO, interfacce e modelli di impostazioni. È il confine tra API, infrastruttura e test.
- `src/OnlyRag.Infrastructure`: SQLite cifrato tramite SQLCipher/EF Core, schema locale gestito da `LocalSqliteSchemaInitializer`, repository, ingestione, retrieval, Qdrant, OCR, ONNX DirectML, immagini, export e sicurezza delle credenziali.
- `src/OnlyRag.Worker`: astrazioni della coda locale e stato dei job in background.
- `src/OnlyRag.Web`: React 19 + TypeScript + Vite. Usa React Query, SignalR, Lucide, React Markdown, CSS globale suddiviso in `src/styles`; non usa Tailwind. Le sezioni sono caricate lazy da `src/App.tsx`.
- `tests`: test xUnit per Core/API/Infrastructure e host backend Playwright; `src/OnlyRag.Web` contiene test Vitest/Testing Library e test e2e Playwright.

Flussi principali:

- I documenti, i job, l'OCR, gli embedding e le traduzioni passano da servizi e code asincrone; gli aggiornamenti usano SignalR con polling REST di riserva.
- Il retrieval combina SQLite FTS5, Qdrant, query transformation, RRF, re-ranking ONNX con fallback euristico, risoluzione Parent-Child, grafo e valutazione CRAG.
- L'OCR espone il bridge Python PaddleOCR e il motore C# ONNX DirectML; la disponibilità GPU/runtime è verificata dalla diagnostica.
- I provider LLM sono Ollama locale oppure i provider Cloud definiti da `CloudLlmProvider`; le chiavi sono salvate tramite il vault Windows/DPAPI, non nei file di configurazione.
- I percorsi dati applicativi sono sotto `%LOCALAPPDATA%\OnlyRag`; l'installer e Qdrant hanno manifest verificati dagli script in `scripts/`.

## 3. Caricamento delle skill

Prima di modificare un sottosistema, carica la skill più stretta applicabile leggendo il relativo `skills\<nome>\SKILL.md` con lo strumento di lettura file del runtime (`view`). Per lavoro trasversale usa `skills\onlyrag\SKILL.md`; per manutenzione usa `code-maintenance-automation`; per C#/WPF/API usa `dotnet-wpf-minimal-api`; per frontend usa `react-vite-frontend`; per retrieval, agenti, immagini o packaging usa rispettivamente `rag-vector-retrieval`, `autonomous-agent-engine`, `onnx-directml-image-gen` o `windows-packaging-signing`.

Le sezioni “Official sources” delle skill devono contenere esclusivamente documentazione primaria del vendor o del progetto mantenuto: Microsoft Learn, documentazione ufficiale del progetto o standard dell'autorità che lo pubblica. Non aggiungere blog, aggregatori, snippet copiati o affermazioni comparative non verificabili.

## 4. Pattern obbligatori

- C#: nullable reference types e implicit usings sono attivi; usa record per DTO immutabili, interfacce per confini sostituibili e dependency injection tramite i metodi `AddOnlyRag*`.
- Mantieni le classi `partial` di `InProcessBackend` separate per feature/endpoint. Aggiungi una route nel file della feature e registrala nel mapping, senza creare un secondo meccanismo di routing.
- Usa `async`/`await` con `CancellationToken` nelle operazioni I/O e nei job. Non bloccare thread con `.Result` o `.Wait()` salvo i punti di bootstrap già esistenti e motivati.
- Propaga gli errori o trasformali nei tipi di errore utente esistenti; non aggiungere catch generici che nascondono fallimenti.
- Nel frontend mantieni controller e hook separati dalla presentazione, usa i tipi API esistenti, esponi loading/error/empty state e assegna nomi ARIA agli elementi interattivi.
- Quando un contratto API cambia, aggiorna nello stesso cambiamento il record in `src/OnlyRag.Core`, il mapping endpoint e i tipi/fixture/test frontend interessati.

## 5. Moduli bloccati e file generati

Non modificare manualmente questi output o fonti senza aggiornare il relativo generatore:

- `assets/brand/**`, `src/OnlyRag.Web/public/**` e `.github/assets/**`: rigenerare con `pwsh .\scripts\Generate-BrandAssets.ps1`; la fonte grafica è `src/OnlyRag.App/Assets/OnlyRag.svg`.
- `src/OnlyRag.Web/dist/**`: output della build Vite, prodotto da `pwsh .\scripts\Build-Web.ps1`; non committarlo né usarlo come fonte.
- `src/OnlyRag.Web/src/api/generated/**`: output opzionale di `npm run openapi:generate`; non correggerlo a mano.
- `packaging/qdrant/payload/**`, `artifacts/**`, `bin/**`, `obj/**`, `node_modules/**`, `test-results/**` e `playwright-report/**`: output/cache locali, non sorgenti.

Tratta con cautela questi confini:

- `src/OnlyRag.Infrastructure/Storage/LocalSqliteSchemaInitializer.cs` è la fonte dello schema locale. Ogni modifica persistente deve includere versione target, percorso di migrazione non distruttivo e test; non riscrivere le versioni storiche per “ripulire” il codice.
- `src/OnlyRag.Core/*Contracts.cs` è il contratto condiviso. Non introdurre DTO duplicati in API o frontend e non modificare un solo lato del contratto.
- I validator di sicurezza (`McpSecurityValidator`, `CloudLlmEndpointValidator`, guard dello storage/workspace e policy agente) non devono essere bypassati per comodità o test.
- `scripts/ocr/runtime-manifest.json` e `packaging/qdrant/manifest.json` sono fonti di verifica degli asset/runtime: modifica solo insieme ai checksum e ai test/installer che li consumano.

## 6. Formattazione e stile

- Windows/PowerShell: usa PowerShell 7 (`pwsh`) e percorsi Windows quotati. Gli script dichiarano il proprio requisito; non assumere che ogni script supporti la stessa versione.
- C#: 4 spazi, file-scoped namespaces, tipi espliciti quando il tipo è evidente, `var` solo dove conforme a `.editorconfig`; usa terminologia e naming già presenti.
- JSON, XML, YAML, Markdown, CSS e TypeScript: 2 spazi, UTF-8, newline finale, righe coerenti con `.editorconfig`.
- Frontend: Prettier con `printWidth: 120` e `trailingComma: none`; ESLint vieta variabili inutilizzate salvo prefisso `_`. Preferisci le utility/classi CSS e i token esistenti alle nuove regole globali.
- Commenta solo vincoli o decisioni non ovvie. Non aggiungere commenti descrittivi ridondanti.

## 7. Verifica incrementale e comandi canonici

Esegui controlli in sequenza, mai in parallelo. Se un controllo fallisce, fermati, correggi la causa e ripetilo prima di procedere:

```powershell
# Controlli frontend mirati
Push-Location .\src\OnlyRag.Web
npm run typecheck
npm run lint
npm run format:check
npm run test:unit
npm run build
Pop-Location

# Suite compatta per agenti: Vitest + xUnit selezionati, seriale
pwsh .\scripts\test-agent.ps1

# Gate rapido: preflight, manifest, frontend e build .NET; salta i test
pwsh .\scripts\Invoke-Gate.ps1 -Fast

# Gate completo prima di una release
pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release
```

Usa `pwsh .\scripts\Test-Code.ps1 -Fast` per la suite locale compatta e `-IncludeIntegration` solo quando l'integrazione lenta è necessaria. `pwsh .\scripts\test-agent.ps1` è il runner AI a output sintetico; `-Full` include la suite completa. Esegui `-IncludeInstaller`, `-IncludeAudits` o `-IncludeRetrievalEval` solo quando la richiesta riguarda packaging, sicurezza o benchmark.

## 8. Sicurezza e igiene del repository

- Non stampare, salvare o committare token, API key, certificati privati, PFX o dati utente. Usa il vault Windows/DPAPI e variabili d'ambiente solo per override locali documentati (`ONLYRAG_WEB_DEV_SERVER`, `ONLYRAG_LIBREOFFICE_PATH`).
- Non aggiungere dipendenze senza aggiornare manifest e lockfile, né modificare versioni bloccate senza una richiesta esplicita.
- Non committare output generati o directory di build. Prima della consegna controlla `git status --short` e `git diff --check`.
