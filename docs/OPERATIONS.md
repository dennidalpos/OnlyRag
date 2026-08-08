# Operazioni e Handoff

Le operazioni di OnlyRag sono Windows-first. Eseguire tutti i comandi dalla radice del repository tramite PowerShell 7 (`pwsh`), a meno che un comando non cambi esplicitamente directory.

## Prerequisiti

Necessari per lo sviluppo:

- Windows 10 versione 1809/build 17763 o più recente, oppure Windows 11.
- PowerShell 7 (`pwsh`).
- SDK .NET 10 selezionato tramite [`global.json`](../global.json).
- Node.js `^20.19.0 || >=22.12.0` con npm.
- Runtime Microsoft Edge WebView2.
- Browser Microsoft Edge per i test e2e Playwright del frontend.

Opzionali per funzionalità specifiche:

- Ollama per chat, embedding e traduzioni locali.
- LibreOffice per l'esportazione PDF delle traduzioni.
- Python da 3.10 a 3.13 per il provisioning dell'engine OCR PaddleOCR.
- Download integrato dei modelli per la generazione di immagini.
- NSIS 3.x per la creazione degli installer di rilascio.
- `signtool.exe` (Windows 10/11 SDK) e un certificato di firma di codice valido per installer firmati.

## Configurazione Iniziale

```powershell
pwsh .\scripts\Bootstrap-Prerequisites.ps1
```

Il bootstrap verifica i prerequisiti dell'host, crea `%LOCALAPPDATA%\OnlyRag`, ripristina i pacchetti .NET, installa le dipendenze frontend, controlla la disponibilità opzionale di Ollama/LibreOffice, prepara lo storage dei modelli di immagini e configura l'OCR quando Python è disponibile. Non compila, impacchetta, firma, installa o rilascia l'app.

Opzioni per limitare il bootstrap:

- `-SkipNode`: salta il controllo Node/npm e l'installazione delle dipendenze web.
- `-SkipOcr`: salta il provisioning OCR.
- `-SkipOllamaCheck`: salta i controlli dell'endpoint Ollama.
- `-SkipImageGenerationCheck`: salta i controlli dello storage dei modelli di immagini.
- `-NonInteractive`: evita prompt ed azioni a livello di sistema.
- `-LibreOfficePath <path>`: specifica un percorso custom per `soffice.exe`.

## Sviluppo e Avvio

Asset web statici:

```powershell
pwsh .\scripts\Build-Web.ps1
dotnet run --project .\src\OnlyRag.App\OnlyRag.App.csproj --configuration Debug
```

Server di sviluppo Vite:

```powershell
Set-Location .\src\OnlyRag.Web
npm run dev
```

In un'altra sessione PowerShell:

```powershell
$env:ONLYRAG_WEB_DEV_SERVER = "http://127.0.0.1:5173"
dotnet run --project .\src\OnlyRag.App\OnlyRag.App.csproj --configuration Debug
```

`ONLYRAG_WEB_DEV_SERVER` accetta unicamente URL loopback `http` o `https` privi di credenziali integrate.

## Mappa dei Comandi

| Attività | Comando |
|---|---|
| Configurazione iniziale | `pwsh .\scripts\Bootstrap-Prerequisites.ps1` |
| Avvio applicazione desktop | `dotnet run --project .\src\OnlyRag.App\OnlyRag.App.csproj --configuration Debug` |
| Avvio dev server Vite | `Set-Location .\src\OnlyRag.Web; npm run dev` |
| Gate di verifica rapido | `pwsh .\scripts\Invoke-Gate.ps1 -Fast` |
| Gate di verifica completo | `pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release` |
| Suite test agente rapida | `pwsh .\scripts\test-agent.ps1` |
| Benchmark di recupero RAG | `pwsh .\scripts\Evaluate-Retrieval.ps1 -DatasetPath .\docs\retrieval-evaluation.sample.json` |
| Verifica prontezza installer | `pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release -IncludeInstaller` |
| Controlli frontend | `Set-Location .\src\OnlyRag.Web; npm run typecheck; npm run lint; npm run format:check; npm run test` |
| Test .NET | `dotnet test .\OnlyRag.sln --configuration Release` |
| Compilazione UI web | `pwsh .\scripts\Build-Web.ps1` |
| Compilazione app desktop | `pwsh .\scripts\Build-App.ps1 -Configuration Release` |
| Compilazione installer non firmato | `pwsh .\scripts\Build-Installer.ps1 -Configuration Release` |
| Firma installer | `pwsh .\scripts\Sign-Release.ps1 -CertificateThumbprint <thumbprint>` |
| Verifica ciclo di vita installer | `pwsh .\scripts\Test-InstallerRelease.ps1 -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe -RequireSigned -RunInstallLifecycle` |
| Pulizia output generati | `pwsh .\scripts\Clean.ps1` |

## Gate di Verifica

Gate di prontezza rapida (preflight, typecheck, lint, build e manifest):

```powershell
pwsh .\scripts\Invoke-Gate.ps1 -Fast
```

Prontezza completa dell'applicazione:

```powershell
pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release
```

Il gate esegue i controlli preflight, il ripristino delle dipendenze, typecheck/lint/format/test del frontend, test .NET, self-test prerequisiti installer, manifest OCR, build web e build .NET.

Prontezza per la creazione del pacchetto installer:

```powershell
pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release -IncludeInstaller -IncludeRetrievalEval
```

La CI GitHub Actions esegue `pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release -IncludeAudits` su runner `windows-latest`.

## Compilazione e Packaging

Build desktop:

```powershell
pwsh .\scripts\Build-App.ps1 -Configuration Release
```

Build installer:

```powershell
pwsh .\scripts\Build-Installer.ps1 -Configuration Release
```

## Procedura di Rilascio (Handoff)

1. Eseguire il gate per l'installer:

   ```powershell
   pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release -IncludeInstaller
   ```

2. Compilare e firmare con il certificato:

   ```powershell
   pwsh .\scripts\Sign-Release.ps1 -CertificateThumbprint <thumbprint>
   ```

   Oppure tramite file PFX esterno:

   ```powershell
   pwsh .\scripts\Sign-Release.ps1 -CertificatePath "C:\Path\To\OnlyRag-CodeSigning.pfx"
   ```

3. Verificare il ciclo di vita su una macchina di test pulita:

   ```powershell
   pwsh .\scripts\Test-InstallerRelease.ps1 -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe -RequireSigned -RunInstallLifecycle
   ```

## Configurazione del Runtime

Perquisiti di variabile d'ambiente obbligatori: nessuno.

Variabili d'ambiente opzionali:

- `ONLYRAG_WEB_DEV_SERVER`: URL di sviluppo loopback per WebView2.
- `ONLYRAG_LIBREOFFICE_PATH`: Percorso completo di `soffice.exe` per l'esportazione PDF delle traduzioni.

## Percorsi Locali

- `%LOCALAPPDATA%\OnlyRag`: Documenti, database SQLite, vettori Qdrant, job, impostazioni, storico chat, cache OCR, log e profili WebView2.
- `%LOCALAPPDATA%\OnlyRag\backups`: Backup timestamped creati prima dei reset totali confermati.
- `%LOCALAPPDATA%\Programs\OnlyRag`: Percorso di installazione predefinito dell'applicazione.

## Pulizia Workspace

```powershell
pwsh .\scripts\Clean.ps1
```

`Clean.ps1` rimuove tutti gli output di build, gli artefatti e le dipendenze temporanee mantenendo intatti i file sorgente tracciati da Git.

