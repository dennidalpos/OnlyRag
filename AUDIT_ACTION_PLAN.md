# AUDIT_ACTION_PLAN.md

## Priorita immediate

1. Sistemare job pause/resume.
   - Gravita: alta.
   - Sforzo: medio.
   - Rischio se non si interviene: job cancellati o duplicati sotto carico reale.
   - Test dopo intervento: pause running job lento, resume immediato, resume dopo cancellazione effettiva, doppio click, restart durante Pausing.

2. Rendere atomica la deduplica import.
   - Gravita: alta.
   - Sforzo: medio/alto se serve migrazione.
   - Rischio se non si interviene: record multipli puntano allo stesso file; delete/reindex rompe dati validi.
   - Test dopo intervento: import concorrente stesso file, import batch con duplicati, delete di un duplicato, migrazione DB esistente con duplicati.

3. Correggere I/O e timeout del bridge OCR.
   - Gravita: alta.
   - Sforzo: medio.
   - Rischio se non si interviene: OCR/provisioning appesi senza recovery.
   - Test dopo intervento: bridge che scrive molto stderr, bridge hung, timeout prepare, timeout recognize, chiusura app durante OCR.

4. Rimuovere materiale di firma dal workspace repo.
   - Gravita: alta.
   - Sforzo: basso.
   - Rischio se non si interviene: esposizione PFX/password tramite backup, zip, screen sharing o automazioni.
   - Test dopo intervento: `git status --ignored`, `scripts/Sign-Release.ps1` con path esterno o env var, verifica che nessun file segreto resti sotto repo.

5. Rivedere installazione Ollama via `irm | iex`.
   - Gravita: alta.
   - Sforzo: basso/medio.
   - Rischio se non si interviene: esecuzione remota non pinning, incompatibile con policy enterprise.
   - Test dopo intervento: flusso UI per installazione/manuale, messaggi per offline, nessuna esecuzione remota automatica non verificata.

## Interventi consigliati in ordine

1. Introdurre stato job `Pausing` o lease owner.
   - Bloccare `/resume` se il job e ancora registrato in `RunningJobCancellationRegistry`.
   - Trattare pause come richiesta cooperativa distinta da cancellazione definitiva.
   - Aggiungere test di concorrenza con handler lento.

2. Aggiungere vincolo di unicita su `documents.sha256` per valori non nulli.
   - Prima scrivere script di audit/migrazione per individuare duplicati esistenti.
   - Decidere merge o quarantena dei record duplicati.
   - Aggiornare `LocalDocumentLibraryService.ImportAsync` per gestire conflitto DB.

3. Rifattorizzare process runner condiviso.
   - Usare lettura parallela stdout/stderr.
   - Timeout per check, prepare, recognize e provisioning.
   - Kill process tree su cancellazione.
   - Log sintetici senza stampare output sensibile completo.

4. Separare gestione segreti firma.
   - Vietare default PFX auto-discovery in `certificates/app`.
   - Documentare storage esterno.
   - Conservare in repo solo `.gitkeep` e README.

5. Proteggere o ridurre `/api/health`.
   - Lasciare `/health` minimale non autenticato.
   - Portare vector health sotto endpoint autenticato.

6. Stabilizzare contratti API.
   - OpenAPI o contract tests JSON.
   - Test runtime che confrontino DTO C# e TypeScript critici.

7. Aggiungere test frontend.
   - Component test per Chat, Documents, Settings.
   - E2E smoke per import mock, job status, errore backend, modal preview.
   - Test accessibilita base con axe, gia presente tra devDependencies.

8. Ridurre file sovradimensionati.
   - SettingsSection: estrarre pannelli Ollama/OCR/Office/Performance.
   - DocumentIngestionService: strategie per TXT/PDF/Image/Office.
   - InProcessBackend: composizione DI/middleware/helper in moduli dedicati.

9. Migliorare UX stale/offline.
   - Badge ultimo aggiornamento.
   - Errori polling visibili dopo soglia.
   - Exit flow non deve interpretare errore API come zero job.

10. Definire release gate separato.
    - Gate codice automatico.
    - Gate installer firmato.
    - Gate interattivo WPF/WebView/Ollama/OCR.

## Rischi se non si interviene

- Perdita o corruzione dati locali in scenari concorrenti.
- Job lunghi non recuperabili o duplicati.
- UI che comunica successo mentre backend e stale o in errore.
- Release percepita come verificata solo perche build/test passano.
- Segreti di firma gestiti in modo troppo vicino al repository.
- Difficolta crescente nel modificare Settings/Ingestion/Backend senza regressioni.

## Cosa testare dopo ogni intervento

- Job:
  - pause running;
  - resume immediato;
  - cancel running;
  - restart durante running;
  - maxParallelJobs > 1.

- Import:
  - stesso file due volte in parallelo;
  - stesso file in batch;
  - batch con primo file valido e secondo invalido;
  - delete dopo dedup;
  - reindex dopo delete di altro record.

- OCR/processi:
  - stderr grande;
  - stdout invalido;
  - processo hung;
  - timeout;
  - chiusura app durante OCR.

- Sicurezza:
  - API senza token;
  - `/api/health`;
  - provisioning con conferma falsa;
  - materiale certificato assente dalla repo.

- UI:
  - backend offline durante polling;
  - storage WebView corrotto;
  - import fallito parziale;
  - modal preview su documento senza pagine;
  - form settings con input limite.

- Release:
  - `pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release`;
  - installer con `-IncludeInstaller`;
  - firma Authenticode;
  - install/uninstall su macchina pulita;
  - app WPF avviata da installazione.
