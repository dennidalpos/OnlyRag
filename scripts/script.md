# Script Inventory

Run repository scripts from the repository root in PowerShell 7 unless a script documents another working directory. Generated outputs under `bin`, `obj`, `dist`, `node_modules`, and `artifacts` are not source script locations.

| Nome script | Percorso | Funzione | Quando usarlo | Invocato da | Dipendenze/prerequisiti | Note |
|---|---|---|---|---|---|---|
| `Bootstrap-Prerequisites.ps1` | `scripts\Bootstrap-Prerequisites.ps1` | Verifica prerequisiti Windows, crea directory dati locali, esegue restore .NET, installa dipendenze web e prepara OCR opzionale. | Setup sviluppo o verifica fresh-install del repository. | `README.md`, `docs\OPERATIONS.md`, documentazione OCR. | Windows, PowerShell 7, .NET 10 SDK/runtimes, WebView2, Node/npm; opzionali Python, Ollama, LibreOffice. | Non builda, non pacchettizza, non firma e non rilascia. |
| `Build-Web.ps1` | `scripts\Build-Web.ps1` | Esegue install npm da lockfile e build React/Vite. | Quando servono asset statici aggiornati in `src\OnlyRag.Web\dist`. | `README.md`, `docs\OPERATIONS.md`, `Build-Installer.ps1`, `Invoke-Gate.ps1`. | Node.js/npm compatibili con `src\OnlyRag.Web\package.json`. | Usa `scripts\support\BuildSupport.ps1`. |
| `Build-App.ps1` | `scripts\Build-App.ps1` | Esegue `dotnet restore` e `dotnet build` su `OnlyRag.sln`. | Build .NET locale Debug o Release. | `README.md`, `docs\OPERATIONS.md`, `Invoke-Gate.ps1`. | .NET 10 SDK. | Non esegue test. |
| `Invoke-Gate.ps1` | `scripts\Invoke-Gate.ps1` | Gate canonico: preflight, restore web, restore .NET, typecheck web, test .NET, build web e build .NET; opzionalmente installer. | Prima di PR/release candidate locale o per replicare la catena CI. | `.github\workflows\ci.yml`, `README.md`, `docs\OPERATIONS.md`. | Windows, PowerShell 7, .NET 10 SDK, Node/npm; Inno Setup solo con `-IncludeInstaller`. | Sostituisce `Test-All.ps1` e il precedente gate agent locale. Fallisce al primo errore. |
| `Build-Installer.ps1` | `scripts\Build-Installer.ps1` | Build web, publish WPF self-contained `win-x64`, validazione payload e compilazione installer Inno Setup. | Creazione installer non firmato o firmato tramite thumbprint. | `README.md`, `docs\OPERATIONS.md`, `packaging\README.md`, `Sign-Release.ps1`, opzionalmente `Invoke-Gate.ps1 -IncludeInstaller`. | .NET 10 SDK, Node/npm, Inno Setup 6; opzionali `signtool.exe` e certificato. | Usa `scripts\support\BuildSupport.ps1`. |
| `Test-InstallerPrerequisites.ps1` | `scripts\Test-InstallerPrerequisites.ps1` | Verifica il prerequisito bloccante WebView2 e contiene un self-test simulato per prerequisito presente/assente e messaggio atteso. | Validazione setup/preflight senza installare. | `Invoke-Gate.ps1`, `README.md`, `docs\OPERATIONS.md`, `packaging\README.md`. | PowerShell 7; Windows per rilevazione reale del runtime. | Il setup Inno usa la stessa strategia: blocca solo WebView2 perché .NET è incluso nel publish self-contained. |
| `Sign-Release.ps1` | `scripts\Sign-Release.ps1` | Importa/usa certificato, invoca build installer firmata, verifica firma e avvia verifica release firmata. | Release candidate firmate. | `docs\SIGNING.md`, `PROJECT_STATUS.json`. | Certificato code-signing, Windows SDK `signtool.exe`, Inno Setup 6, prerequisiti build. | Rimuove il certificato importato temporaneamente salvo `-KeepImportedCertificate`. |
| `Test-InstallerRelease.ps1` | `scripts\Test-InstallerRelease.ps1` | Produce evidenza JSON per installer; opzionalmente esegue lifecycle install/upgrade/uninstall/rollback. | Verifica release installer, prima non invasiva e poi su macchina pulita con `-RunInstallLifecycle`. | `README.md`, `docs\OPERATIONS.md`, `docs\SIGNING.md`, `packaging\README.md`, `Sign-Release.ps1`. | Installer `.exe`; opzionali firma valida, macchina Windows pulita e installer upgrade/rollback. | Usa `scripts\support\BuildSupport.ps1`. |
| `Generate-BrandAssets.ps1` | `scripts\Generate-BrandAssets.ps1` | Rigenera asset visuali brand sotto `assets\brand` e aggiorna copie integrate per web, GitHub README e social metadata. | Quando cambia `src\OnlyRag.App\Assets\OnlyRag.svg` o servono asset social/setup aggiornati. | `assets\brand\README.md`, `docs\BRAND_ASSETS.md`. | Windows, PowerShell 7, assembly WPF `PresentationCore`/`WindowsBase`. | Spostato da `assets\brand`; gli output sorgente restano in `assets\brand`, le copie consumate restano nei rispettivi path applicativi. |
| `BuildSupport.ps1` | `scripts\support\BuildSupport.ps1` | Funzioni condivise per build web, packaging, signing, validazione payload e rimozioni robuste. | Solo import interno da altri script. | `Build-Web.ps1`, `Build-Installer.ps1`, `Sign-Release.ps1`, `Test-InstallerRelease.ps1`. | PowerShell 7; prerequisiti specifici dipendono dalla funzione chiamata. | Helper interno, non destinato all’uso diretto. |
| `paddle_ocr_bridge.py` | `scripts\ocr\paddle_ocr_bridge.py` | Bridge runtime OCR PaddleOCR invocato dall’app e verificato dal bootstrap. | Runtime OCR o check ambiente OCR. | `Bootstrap-Prerequisites.ps1`, codice applicativo tramite configurazione OCR, copy MSBuild da `OnlyRag.Infrastructure.csproj`. | Python 3.10+ e pacchetti in `scripts\ocr\requirements.txt`. | Cartella runtime copiata negli output build/publish; non è un comando sviluppatore primario. |

## Script Package Manager

`src\OnlyRag.Web\package.json` definisce gli script npm reali del frontend:

| Nome script | Percorso | Funzione | Quando usarlo | Invocato da | Dipendenze/prerequisiti | Note |
|---|---|---|---|---|---|---|
| `dev` | `src\OnlyRag.Web\package.json` | Avvia Vite su `127.0.0.1`. | Sviluppo UI con desktop app in Debug. | Documentazione operativa. | Node/npm e dipendenze installate. | Working directory: `src\OnlyRag.Web`. |
| `build` | `src\OnlyRag.Web\package.json` | Esegue `tsc -b` e `vite build`. | Build asset web statici. | `Build-Web.ps1`, `Build-Installer.ps1`. | Node/npm e dipendenze installate. | Output in `src\OnlyRag.Web\dist`. |
| `typecheck` | `src\OnlyRag.Web\package.json` | Esegue TypeScript senza emit. | Verifica frontend. | `Invoke-Gate.ps1`, documentazione operativa. | Node/npm e dipendenze installate. | Nessun lint separato definito. |
| `preview` | `src\OnlyRag.Web\package.json` | Avvia preview Vite su `127.0.0.1`. | Preview manuale degli asset buildati. | Uso manuale. | Build web già prodotta. | Non usato da CI o packaging. |

## Migrazioni

- `scripts\Test-All.ps1` è stato rimosso: usare `scripts\Invoke-Gate.ps1`.
- `scripts\agents\Gate-Build.ps1` è stato rimosso: era un gate locale distruttivo con wipe dati e packaging opzionale. Usare `scripts\Invoke-Gate.ps1`; per packaging aggiungere `-IncludeInstaller`.
- `scripts\internal\BuildSupport.ps1` è stato spostato in `scripts\support\BuildSupport.ps1`.
- `assets\brand\Generate-BrandAssets.ps1` è stato spostato in `scripts\Generate-BrandAssets.ps1`; gli output restano in `assets\brand`.
