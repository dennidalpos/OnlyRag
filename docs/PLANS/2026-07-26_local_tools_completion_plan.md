# Piano di Architettura: Estensione dei Tool Locali per OnlyRag Agent Engine

**Data**: 26 Luglio 2026  
**Stato**: Approvato / In Pianificazione  
**Autore**: Antigravity Agent Engine  

---

## 1. Obiettivo

Integrare **5 nuovi tool nativi** all'interno dell'Agent Engine di **OnlyRag** ([`WorkspaceToolExecutor.cs`](file:///d:/GITHUB/OnlyRag/src/OnlyRag.Infrastructure/Agent/WorkspaceToolExecutor.cs) e [`AgentLoopEngine.cs`](file:///d:/GITHUB/OnlyRag/src/OnlyRag.Api/AgentLoopEngine.cs)) portando l'applicazione ad un totale di **13 tool locali**.

---

## 2. Inventario dei 13 Tool Locali Nativi

| # | Tool | Categoria | Descrizione |
|---|------|-----------|-------------|
| 1 | `list_dir` | Workspace | Esplorazione cartelle ed alberi di progetto |
| 2 | `read_file` / `view_file` | File System | Lettura con numerazione righe e paginazione |
| 3 | `write_file` | File System | Scrittura e creazione file |
| 4 | `replace_file_content` | Refactoring | Sostituzione blocco singolo con normalizzazione a capo |
| 5 | `multi_replace_file_content` | Refactoring | **[NUOVO]** Modifica atomica di più blocchi non contigui nello stesso file |
| 6 | `grep_search` | Codice | Ricerca testo/regex nei file del workspace |
| 7 | `git_diff_inspect` | Git | **[NUOVO]** Ispezione status e diff formattato in Markdown |
| 8 | `run_command` | Shell | Esecuzione comandi PowerShell (sincrona/asincrona) |
| 9 | `manage_task` | Processi | Gestione task in background |
| 10 | `web_search` | Rete | Ricerca online con parser DuckDuckGo HTML |
| 11 | `ingest_office_doc` | RAG 2.0 | **[NUOVO]** Ingestion Office (LibreOffice PDF + PaddleOCR GPU + Chunking) |
| 12 | `generate_image_onnx` | Generativo | **[NUOVO]** Generazione/editing immagini via ONNX DirectML GPU |
| 13 | `query_retrieval_index` | Retrieval | **[NUOVO]** Query diretta su indici SQLite FTS5 e Qdrant vectors |

---

## 3. Impatto Architetturale & File Coinvolti

1. **`src/OnlyRag.Infrastructure/Agent/WorkspaceToolExecutor.cs`**:
   - Aggiunta dei 5 metodi di esecuzione nativi con convalida rigida `ResolveSafePath`.
2. **`src/OnlyRag.Api/AgentLoopEngine.cs`**:
   - Aggiornamento di `GetSystemPrompt()` e `NormalizeToolName()`.
3. **`tests/OnlyRag.Infrastructure.Tests/WorkspaceToolExecutorTests.cs`**:
   - Nuova suite di unit test per la copertura del 100% sui nuovi tool.

---

## 4. Verifica e Gate Canonico

```powershell
pwsh .\scripts\Lint-Code.ps1
pwsh .\scripts\Test-Code.ps1
pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release
```
