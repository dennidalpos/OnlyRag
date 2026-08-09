# Script PowerShell

Eseguire tutti gli script PowerShell dalla radice del repository tramite PowerShell 7 (`pwsh`). Gli script di supporto sotto `scripts\support` sono helper interni.

## Flussi Canonici

Configurazione iniziale:

```powershell
pwsh .\scripts\Bootstrap-Prerequisites.ps1
```

Controllo di prontezza dell'applicazione:

```powershell
# Gate rapido (preflight, typecheck, lint, build, manifest)
pwsh .\scripts\Invoke-Gate.ps1 -Fast

# Gate di rilascio completo con test
pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release

# Gate con audit di sicurezza delle dipendenze
pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release -IncludeAudits
```

Build locale e avvio:

```powershell
pwsh .\scripts\Build-Web.ps1
pwsh .\scripts\Build-App.ps1 -Configuration Release
dotnet run --project .\src\OnlyRag.App\OnlyRag.App.csproj --configuration Debug
```

Pulizia workspace:

```powershell
pwsh .\scripts\Clean.ps1
```

## Inventario degli Script Pubblici

| Script | Percorso | Scopo | Quando usare |
|---|---|---|---|
| Format Code | `scripts\Format-Code.ps1` | Formatta la soluzione C# .NET e il frontend React (Prettier). | Sviluppo di routine, pre-commit. |
| Lint Code | `scripts\Lint-Code.ps1` | Esegue ESLint, typecheck TypeScript e analizzatori .NET. | Convalida della qualità del codice. |
| Test Code | `scripts\Test-Code.ps1` | Esegue i test Vitest del frontend e i test xUnit della soluzione .NET. | Esecuzione test automatizzata. |
| Test Agent Fast Mode | `scripts\test-agent.ps1` | Esegue la suite di test rapida ottimizzata per agenti AI (output sintetico PASS/FAIL). | Verifica rapida per agenti AI. |
| Evaluate Retrieval | `scripts\Evaluate-Retrieval.ps1` | Calcola metriche di benchmark di recupero RAG (Recall@K, MRR, contesto). | Valutazione qualità RAG. |
| Bootstrap Prerequisites | `scripts\Bootstrap-Prerequisites.ps1` | Verifica i prerequisiti di sviluppo Windows e ripristina le dipendenze. | Configurazione iniziale o ripristino. |
| Build Web UI | `scripts\Build-Web.ps1` | Esegue la build di produzione Vite del frontend. | Prima della build desktop. |
| Build App | `scripts\Build-App.ps1` | Compila gli asset web e compila l'app desktop. | Build desktop locale. |
| Repository Gate | `scripts\Invoke-Gate.ps1` | Esegue il gate di verifica canonico prima del commit o rilascio. | Verifica della prontezza. |
| Build Installer | `scripts\Build-Installer.ps1` | Pubblica l'app `win-x64` e compila l'installer NSIS. | Creazione candidato installer. |
| Sign Release | `scripts\Sign-Release.ps1` | Firma digitalmente l'installer tramite `signtool.exe`. | Firma per rilascio. |
| Test Installer Release | `scripts\Test-InstallerRelease.ps1` | Verifica la firma e il ciclo di vita dell'installer. | Verifica di rilascio. |
| Test Installer Prerequisites | `scripts\Test-InstallerPrerequisites.ps1` | Convalida i prerequisiti di sistema ed NSIS per la compilazione dell'installer. | Preflight build installer. |
| Export Enterprise Certificate | `scripts\Export-EnterpriseSigningCertificate.ps1` | Esporta il certificato pubblico di firma per il trust aziendale. | Distribuzione trust aziendale. |
| Test Enterprise Trust | `scripts\Test-EnterpriseSigningTrust.ps1` | Verifica la catena di trust del certificato di firma aziendale. | Verifica trust certificato. |
| Generate Brand Assets | `scripts\Generate-BrandAssets.ps1` | Genera asset grafici, icone e grafiche di setup dal sorgente SVG. | Aggiornamento branding. |
| Download Qdrant | `scripts\Download-Qdrant.ps1` | Scarica e verifica l'eseguibile Qdrant dal manifest. | Quando il payload manca. |
| Clean Generated Outputs | `scripts\Clean.ps1` | Rimuove tutti gli output di build, gli artefatti e la cache. | Pulizia locale. |


