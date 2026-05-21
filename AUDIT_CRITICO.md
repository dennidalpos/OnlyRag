# AUDIT_CRITICO.md

## Executive summary

Il progetto ha una base tecnica seria: build Release pulita, gate canonico verde, test backend numerosi, SQLite locale con migrazioni, token sessione per API locali e pipeline RAG/OCR/traduzione abbastanza strutturata. Questo non basta per dichiararlo affidabile.

I problemi peggiori non emergono da build/test: sono concorrenza job, coerenza DB/filesystem, processi esterni OCR/provisioning, gestione segreti di firma e copertura UI/release insufficiente. Il codice "sembra funzionare" nei percorsi lineari, ma resta fragile in scenari realistici: doppio click, import parallelo, OCR rumoroso o bloccato, storage locale corrotto, backend stale, release installer.

Giudizio operativo: il gate automatico dimostra compilabilita, non affidabilita end-to-end.

## Stack rilevato

- Runtime principale: .NET 10 (`net10.0`, `net10.0-windows`), SDK locale audit `10.0.300`.
- App desktop: WPF + Microsoft WebView2.
- Backend: ASP.NET Core Minimal API in-process su loopback Kestrel.
- Frontend: React 19, TypeScript 5.9, Vite 7, npm.
- Persistenza: SQLite via `Microsoft.Data.Sqlite`, WAL, foreign keys, busy timeout.
- Vector search: `sqlite-vec`.
- OCR: Python PaddleOCR bridge in `scripts/ocr`.
- Office: DocumentFormat.OpenXml, PdfPig, LibreOffice opzionale.
- Test: xUnit per Core/Infrastructure/API; nessun test frontend.
- CI: GitHub Actions Windows, `scripts/Invoke-Gate.ps1 -Configuration Release`.
- Package manager frontend: npm (`package-lock.json`).
- Packaging: Inno Setup, script signing Authenticode.

## Comandi eseguiti

| Comando | Risultato |
|---|---|
| `git status --short` | Pulito per file tracciati prima dell'audit. |
| `dotnet --info` | OK. SDK 10.0.300, nessun `global.json`. |
| `node --version; npm --version` | OK. Node v22.22.3, npm 10.9.8. |
| `dotnet restore .\OnlyRag.sln` | OK. |
| `npm ci` | OK. 152 package, 0 vulnerabilita npm. |
| `dotnet build .\OnlyRag.sln --configuration Release --no-restore` | OK. 0 warning, 0 errori. |
| `npm run typecheck` | OK. |
| `npm run lint` | OK. |
| `npm run format:check` | OK. |
| `dotnet test .\OnlyRag.sln --configuration Release --no-build` | OK. La summary piu affidabile del gate successivo riporta 8 Core + 65 Infrastructure + 92 API = 165 test passati. |
| `npm run build` | OK. Vite build completata. |
| `dotnet list .\OnlyRag.sln package --vulnerable --include-transitive` | OK. Nessun pacchetto vulnerabile segnalato da NuGet. |
| `npm audit --omit=dev` | OK. 0 vulnerabilita. |
| `pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release` | OK. Restore, audit, typecheck, lint, format, test, self-test installer, web build, .NET build. Installer skipped by default. |

Nota critica: il gate ha creato/aggiornato output ignorati (`bin`, `obj`, `dist`, `node_modules`). Non sono sorgenti.

## Problemi ordinati per gravita

### High

1. `AUD-001` - Pausa/ripresa job puo cancellare o duplicare esecuzioni.
2. `AUD-002` - Deduplica import non atomica; record duplicati possono condividere file fisico.
3. `AUD-003` - OCR bridge puo deadlockare su stdout/stderr; check/provisioning senza timeout robusti.
4. `AUD-004` - PFX e file password di signing presenti nella working tree locale ignorata.
5. `AUD-005` - Installazione Ollama esegue `irm ... | iex` con ExecutionPolicy Bypass.

### Medium

6. `AUD-006` - `/api/health` senza token rivela metadati embedding.
7. `AUD-007` - File enormi e responsabilita concentrate oltre le soglie del repo.
8. `AUD-008` - Nessun test frontend/component/e2e.
9. `AUD-009` - Polling UI ignora errori e crea stato stale.
10. `AUD-010` - Import batch non atomico e senza risultato parziale.
11. `AUD-011` - Processi esterni possono restare orfani su cancellazione.
12. `AUD-012` - Chat/draft duplicati in WebView storage senza validazione schema.
13. `AUD-013` - Prompt RAG vulnerabile a prompt injection da documenti.
14. `AUD-014` - Contratti API manuali, non generati e non testati end-to-end.
15. `AUD-015` - Schema SQLite senza CHECK constraint per stati/progress.
16. `AUD-019` - Gate verde non copre installer completo, app desktop interattiva e modelli live.
17. `AUD-020` - Errori esterni/path locali possono arrivare alla UI.

### Low

18. `AUD-016` - Ranking keyword scarta valore BM25 e usa punteggio ordinale.
19. `AUD-017` - Nessun `global.json` per pin SDK .NET.
20. `AUD-018` - Inventario script in `scripts/script.md`, non `scripts/README.md`.

Dettagli completi e machine-readable: `AUDIT_FINDINGS.json`.

## Dubbi e perplessita

- Dubbio: il modello di sicurezza sembra "single-user trusted local UI", ma alcune azioni hanno impatto da process launcher e installazione remota. Questo boundary va scritto chiaramente.
- Dubbio: la deduplica per SHA sembra pensata come unicita logica, ma il DB non la impone.
- Dubbio: pause/resume sembra funzionalita utente, ma la semantica reale e una cancellazione cooperativa con stato Paused. Non e abbastanza deterministica.
- Dubbio: il gate automatico viene percepito come release-readiness, ma salta proprio installer/interattivo.
- Perplessita: `SettingsSection.tsx` da 1574 righe e un rischio concreto di regressioni, non solo una violazione estetica.
- Perplessita: la presenza locale di un PFX e di un file password in una cartella del repo e incompatibile con una disciplina di release seria.

## Gap analysis

### Aree non coperte

- Test frontend React.
- E2E WebView/WPF.
- Accessibilita reale.
- Import concorrente.
- Pause/resume concorrente.
- OCR bridge con stderr grande/hang.
- Provisioning OCR interrotto o rete lenta.
- Prompt injection RAG.
- Installer firmato e lifecycle clean-machine.
- UI con dati popolati reali e Ollama live.

### Aree coperte male

- Release readiness: gate codice buono, release gate incompleto.
- Contratti API: tipizzazione duplicata, nessun contract test.
- UX offline/stale: diversi catch silenziosi.
- Database integrity: molte assunzioni delegate al codice.
- Processi esterni: ogni integrazione ha pattern diverso.

### Aree ambigue

- Semantica di deduplica: stesso contenuto deve essere uno o piu documenti?
- Semantica di pausa: sospensione riprendibile o cancel soft?
- Stato privacy chat: DB backend vs session/local storage.
- Endpoint health: monitor pubblico locale o dato UI autenticato?
- Installazione dipendenze: app deve installare o solo guidare?

### Assunzioni pericolose

- Un solo utente/processo modifica la libreria.
- File originali e record DB restano sempre allineati.
- I processi Python/LibreOffice non saturano pipe e rispettano timeout.
- Il frontend e sempre affidabile e non compromesso.
- Il gate automatico rappresenta abbastanza bene il comportamento release.

### Domande aperte

1. Il PFX locale e materiale reale? Va ruotato?
2. Il prodotto e destinato a consumer, enterprise o entrambi?
3. L'app puo eseguire installer/provisioning remoti in ambienti gestiti?
4. Deduplica deve preservare nomi logici multipli o no?
5. Pause/resume e requisito vero o basta cancel/retry?
6. Il backend locale e API privata o contratto supportato?
7. Quanto deve durare OCR/provisioning prima di timeout?
8. I documenti importati sono considerati fidati?
9. Serve cifratura dati locali o solo local-first?
10. Quali workflow live sono obbligatori prima di release?

### Cose da verificare manualmente

- Avvio WPF da build pulita e da installer.
- WebView2 su macchina senza runtime e con runtime.
- Import PDF scansionato reale.
- Import Office legacy con LibreOffice installato e mancante.
- Chat con Ollama reale lento/offline.
- Traduzione lunga con stop/pause/resume.
- Display scaling installer 100/125/150/200%.
- Firma Authenticode e SmartScreen/trust su macchina pulita.

### Funzionalita apparentemente previste ma incomplete

- Release firmata: tracciata ma bloccata.
- Installer lifecycle: script presente, ma non eseguito nel gate default.
- OCR provisioning: presente, ma non cancellabile/timeout robusto.
- Frontend quality gate: lint/typecheck, ma non test funzionali.
- Health vector: presente, ma auth ambigua.

## Raccomandazioni prioritarie

1. Correggere job pause/resume prima di affidarsi a workflow lunghi.
2. Rendere unica e atomica la deduplica documento.
3. Sistemare process runner OCR/provisioning con timeout e kill.
4. Spostare PFX/password fuori dal repo e ruotare se workspace condiviso.
5. Sostituire `irm | iex` con installazione verificabile o manuale.
6. Proteggere `/api/health` o ridurlo.
7. Aggiungere test frontend/E2E minimi.
8. Separare gate codice da release gate.
9. Avviare refactor mirato dei file giganti.
10. Introdurre contract tests API.

## Quick wins

- Aggiungere `global.json`.
- Rinominare o duplicare `scripts/script.md` in `scripts/README.md`.
- Proteggere `/api/health` con token.
- Aggiungere warning UI su dati stale.
- Limitare storage chat in WebView.
- Leggere rank BM25 reale.
- Aggiungere test per batch import parziale.
- Aggiungere test per stderr grande del bridge OCR.

## Rischi sistemici

- La qualita automatica e backend-heavy: il frontend e release flow restano scoperti.
- La coerenza DB/filesystem non e abbastanza difesa da vincoli.
- I job asincroni non hanno una semantica robusta di lease/pause/resume.
- L'app integra processi esterni potenti senza un unico modello di timeout/cancellazione/log.
- Sicurezza locale e signing sono trattate come operative, ma hanno impatto di prodotto.

## Classificazione finale

Stato progetto: **Rischioso**.

Motivazione sintetica: il codice compila, i test backend passano e il gate e pulito, ma i failure mode importanti sono fuori dalla copertura attuale. I rischi High possono causare perdita di coerenza dati, job errati o esposizione operativa di materiale di firma. Non e bloccante per sviluppo, ma non lo chiamerei pronto per release senza risolvere i primi cinque punti.

## Top 10 problemi da risolvere prima

1. Pause/resume job concorrente.
2. Deduplica import non atomica.
3. OCR bridge deadlock/timeout.
4. PFX/password locali nel repo workspace.
5. Install remoto Ollama via `irm | iex`.
6. Assenza test frontend/E2E.
7. Gate release incompleto.
8. File enormi in Settings/Ingestion/Backend.
9. UI stale per catch silenziosi.
10. Contratti API manuali senza test.

## Top 10 domande da chiarire col proprietario

1. Il PFX locale e reale e va ruotato?
2. Deduplica deve essere per contenuto o per file logico?
3. Pause/resume deve sospendere davvero o solo cancellare e riprendere da checkpoint?
4. L'app puo eseguire comandi di installazione remoti?
5. Quale e il release gate minimo accettabile?
6. I documenti importati sono fidati o potenzialmente ostili?
7. Serve cifratura locale per chat/documenti/metadati?
8. `/api/health` serve a monitor esterni?
9. Quali versioni SDK .NET devono essere supportate?
10. Quali workflow UI devono avere test automatici prima della release?
