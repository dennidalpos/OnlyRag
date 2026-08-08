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
| Bootstrap Prerequisites | `scripts\Bootstrap-Prerequisites.ps1` | Verifica i prerequisiti di sviluppo Windows e ripristina le dipendenze. | Configurazione iniziale o ripristino. |
| Build Web UI | `scripts\Build-Web.ps1` | Esegue la build di produzione Vite del frontend. | Prima della build desktop. |
| Build App | `scripts\Build-App.ps1` | Compila gli asset web, prepara Qdrant e compila l'app desktop. | Build desktop locale. |
| Repository Gate | `scripts\Invoke-Gate.ps1` | Esegue il gate di verifica canonico prima del commit o rilascio. | Verifica della prontezza. |
| Build Installer | `scripts\Build-Installer.ps1` | Pubblica l'app `win-x64` e compila l'installer NSIS. | Creazione candidato installer. |
| Sign Release | `scripts\Sign-Release.ps1` | Firma digitalmente l'installer tramite `signtool.exe`. | Firma per rilascio. |
| Test Installer Release | `scripts\Test-InstallerRelease.ps1` | Verifica la firma e il ciclo di vita dell'installer. | Verifica di rilascio. |
| Download Qdrant | `scripts\Download-Qdrant.ps1` | Scarica e verifica l'eseguibile Qdrant dal manifest. | Quando il payload manca. |
| Clean Generated Outputs | `scripts\Clean.ps1` | Rimuove tutti gli output di build, gli artefatti e la cache. | Pulizia locale. |

