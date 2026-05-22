# Operations

OnlyRag is developed and operated Windows-first. Run repository commands from the repository root
in PowerShell 7 unless a working directory is shown.

## Prerequisites

Required for development:

- Windows 10 1809 or newer.
- PowerShell 7 (`pwsh`).
- .NET 10 SDK.
- Node.js matching `^20.19.0 || >=22.12.0` with npm for `src\OnlyRag.Web`.
- Microsoft Edge WebView2 Runtime.

Required on an end-user machine before setup can complete:

- Windows 10 1809 or newer, or Windows 11.
- Microsoft Edge WebView2 Runtime. The setup blocks before installation when this runtime is missing and explains how to install the official Microsoft Evergreen Runtime and verify it.

The installer blocks explicitly when Windows is older than Windows 10 version 1809/build 17763.
Direct app startup repeats the Windows and WebView2 checks so manual/portable launches fail early
with the same kind of prerequisite message instead of a generic first-window failure.

The installer is self-contained for OnlyRag's required .NET runtime components and packages the
WebView2 SDK loader plus the required `sqlite-vec` native asset (`vec0.dll`). End users do not
need to install .NET 10 separately for the packaged app.

Required for model features:

- Ollama, available locally or on the LAN. Default endpoint: `http://localhost:11434`.

Optional:

- LibreOffice for legacy Office conversion (`.doc`, `.xls`, `.ppt`) and fallback conversion for
  Office files that cannot be read directly.
- Python plus OCR requirements for the PaddleOCR bridge when scanned PDF/image OCR is needed.

## End-user dependency setup

Optional dependencies are configured from **Settings** in the desktop app:

- **Ollama**: when the CLI is missing, the app opens the official Ollama download page
  for manual installation. OnlyRag does not execute remote PowerShell installer scripts.
  In offline or enterprise-managed environments, use an approved software distribution path.
- **LibreOffice**: when missing, the app opens the official LibreOffice download page.
- **OCR**: **Configura OCR** creates or updates `%LOCALAPPDATA%\OnlyRag\ocr-python\.venv`
  and installs the pinned PaddleOCR requirements shipped with the app.

For Ollama endpoints reachable from another trusted LAN machine, configure Ollama network access
with `OLLAMA_HOST` in the Ollama environment/settings, then restart Ollama and set the endpoint in
**Settings > Ollama**. Non-local endpoints are blocked until **Considera attendibile questo endpoint
Ollama non locale** is enabled. Enable it only for an Ollama service you control on a trusted
network because chat, embeddings, and translation send snippets or translation units to that
endpoint.

## Fresh Install From a Clean Checkout

Use this path when setting up the repository on a new Windows development machine:

```powershell
git clone https://github.com/dennidalpos/OnlyRag.git
Set-Location .\OnlyRag
pwsh .\scripts\Bootstrap-Prerequisites.ps1
pwsh .\scripts\Build-Web.ps1
dotnet run --project .\src\OnlyRag.App\OnlyRag.App.csproj --configuration Debug
```

The first run creates `%LOCALAPPDATA%\OnlyRag` automatically. Run the repository gate before
handoff, packaging, or release work:

```powershell
pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release
```

`npm` is the frontend package manager because `src\OnlyRag.Web\package-lock.json` is present.
Do not switch package managers without changing the lockfile and documentation in the same change.

## Developer Bootstrap

```powershell
pwsh .\scripts\Bootstrap-Prerequisites.ps1
```

The bootstrap verifies Windows, PowerShell 7, .NET 10 SDK/runtimes, WebView2 Runtime, Node.js,
npm, optional Ollama reachability, optional LibreOffice, and OCR prerequisites when the bridge is
present. It creates `%LOCALAPPDATA%\OnlyRag`, runs `dotnet restore`, and runs `npm ci` in
`src\OnlyRag.Web` when `package-lock.json` is present.

Useful switches:

```powershell
pwsh .\scripts\Bootstrap-Prerequisites.ps1 -SkipOcr
pwsh .\scripts\Bootstrap-Prerequisites.ps1 -SkipNode
pwsh .\scripts\Bootstrap-Prerequisites.ps1 -SkipOllamaCheck
pwsh .\scripts\Bootstrap-Prerequisites.ps1 -NonInteractive
```

The bootstrap is intended for development and verification. End-user setup should use the app
settings actions above.

## Canonical Commands

| Task | Command | Notes |
|---|---|---|
| Developer setup / dependency install | `pwsh .\scripts\Bootstrap-Prerequisites.ps1` | Verifies prerequisites, restores .NET packages, and installs web dependencies when the lockfile is present. |
| Web dependency install only | `npm ci` from `src\OnlyRag.Web` | npm is the supported package manager; `package-lock.json` is authoritative. |
| .NET build | `pwsh .\scripts\Build-App.ps1` | Runs `dotnet restore` and `dotnet build` for `OnlyRag.sln`. Use `-NoRestore` only after a completed .NET restore in the same workspace. |
| Web build | `pwsh .\scripts\Build-Web.ps1` | Runs `npm ci` when the lockfile exists, then `npm run build`. Use `-SkipInstallWhenUpToDate` only after a completed npm restore in the same workspace. |
| Typecheck | `npm run typecheck` from `src\OnlyRag.Web` | Runs TypeScript without emit. |
| Web lint | `npm run lint` from `src\OnlyRag.Web` | Runs ESLint over the React/Vite workspace. |
| Web format check | `npm run format:check` from `src\OnlyRag.Web` | Runs Prettier plus the frontend text-format checker in check mode without rewriting files. |
| Repository gate | `pwsh .\scripts\Invoke-Gate.ps1` | Runs preflight, web dependency restore, .NET restore, npm production dependency audit, NuGet transitive vulnerability audit, web typecheck, web lint, web format check, .NET tests, web build, and .NET build. Add `-IncludeInstaller` only when Inno Setup verification is required on the current machine. |
| Installer prerequisite self-test | `pwsh .\scripts\Test-InstallerPrerequisites.ps1 -SelfTest` | Simulates present and missing blocking prerequisites and verifies the expected message content. |
| Package installer | `pwsh .\scripts\Build-Installer.ps1` | Builds web, publishes WPF, validates publish output, and compiles Inno Setup installer when `ISCC.exe` is installed. Pass `-SigningCertificateThumbprint` for signed release candidates. |
| Verify installer release | `pwsh .\scripts\Test-InstallerRelease.ps1 -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe` | Produces release evidence without installing. Add `-RunInstallLifecycle` on a clean verification machine to test install, shortcuts, launch, upgrade, uninstall, rollback/downgrade, optional components, and signing status. |

ESLint and Prettier configuration live under `src\OnlyRag.Web`. The gate uses check mode only;
run formatter writes deliberately as a separate local maintenance step when needed.

.NET analyzers are enabled solution-wide through `Directory.Build.props`. Release builds and CI
treat warnings as errors, so warning fixes should land with the change that introduces them.

Security audit commands can also be run directly:

```powershell
Set-Location .\src\OnlyRag.Web
npm audit --omit=dev --audit-level=moderate
Set-Location ..\..
dotnet list .\OnlyRag.sln package --vulnerable --include-transitive --format json
```

The full script inventory is documented in [scripts/README.md](../scripts/README.md). Top-level
scripts are reserved for supported setup, build, test, package, signing, installer verification,
brand asset generation, and the repository gate. Shared helpers live under `scripts\support`.

## CI

GitHub Actions is configured in `.github\workflows\ci.yml`. The `verify` job runs on
`windows-latest`, installs .NET `10.0.x`, installs the current Node.js LTS line with npm cache
backed by `src\OnlyRag.Web\package-lock.json`, then runs:

```powershell
.\scripts\Invoke-Gate.ps1 -Configuration Release
```

## Run Locally

Run with static built UI assets:

```powershell
pwsh .\scripts\Build-Web.ps1
dotnet run --project .\src\OnlyRag.App\OnlyRag.App.csproj --configuration Debug
```

Run with the Vite development server:

```powershell
# Terminal 1
Set-Location .\src\OnlyRag.Web
npm run dev

# Terminal 2, from repository root
dotnet run --project .\src\OnlyRag.App\OnlyRag.App.csproj --configuration Debug
```

In Debug, the WPF shell uses `http://127.0.0.1:5173/` when reachable. Override with
`ONLYRAG_WEB_DEV_SERVER` when needed.

## Local Data

Runtime data lives under `%LOCALAPPDATA%\OnlyRag`:

| Path | Contents |
|---|---|
| `data\onlyrag.db` | SQLite database for documents, chunks, embeddings, jobs, chat history, translations, and settings. |
| `documents\originals\` | Local copies of imported source files. |
| `documents\renders\` | Generated OCR page render assets used by document ingestion. |
| `documents\ocr-cache\` | OCR cache artifacts and metadata referenced by the local SQLite store. |
| `documents\exports\` | Translation export files. |
| `ocr-python\` | PaddleOCR Python environment prepared by **Configura OCR** in Settings or by developer bootstrap. |
| `logs\` | Application log files. |
| `webview2\` | WebView2 user data folder for the installed desktop shell, kept outside the install directory. |
| `temp\` | App-scoped temporary work directories such as Office conversion and PDF export staging. |

Document import is bounded to protect the local machine from storage exhaustion. Default backend
limits are 50 files per import, 100 MB per file, 500 MB per multipart request, a 10 GB local
document-originals quota, and at least 1 GB free disk preserved on the library drive. Requests that
exceed these limits are rejected before files are promoted into `documents\originals\`.

The SQLite schema is initialized automatically at startup for a fresh database. Supported older
OnlyRag schema versions are upgraded in place at startup after creating a pre-migration backup under
`%LOCALAPPDATA%\OnlyRag\data\backups`. Unsupported unversioned schemas are rejected instead of
migrated, and there is no separate migration command.

The in-process backend requires a random per-session API token for every non-health `/api` request.
The WPF shell injects this token into the trusted WebView bridge. Endpoints that launch local
processes, such as opening Explorer, PowerShell, browser downloads, or OCR provisioning, also
require an explicit UI confirmation payload.

## App Exit and Jobs

Closing the WPF window with the standard **X** checks both the WebView lifecycle bridge and the
backend job queue. If unsaved UI work or local jobs are active, OnlyRag asks for confirmation before
exiting. Confirmed exit saves available UI state, cancels `Pending`, `Running`, and `Paused` jobs
through the local backend, waits briefly for running job handlers to stop cooperatively, and then
stops the in-process backend.

OnlyRag only targets its own peer processes during exit. It does not directly terminate unrelated
external processes such as `python.exe`, `soffice.exe`, or Ollama; job handlers receive cancellation
through the app and are responsible for stopping child work they started.

If `OnlyRag.App.exe` remains in Task Manager after the window is closed, first confirm whether it is
an OnlyRag process and whether it still owns child processes:

```powershell
Get-CimInstance Win32_Process |
  Where-Object { $_.Name -eq "OnlyRag.App.exe" } |
  Select-Object ProcessId, ParentProcessId, CreationDate, ExecutablePath, CommandLine
```

If the process has no window and `%LOCALAPPDATA%\OnlyRag\logs\backend.log` ends with
`Stopping in-process backend` but not `In-process backend stopped`, the app is likely blocked while
disposing the in-process backend. Verify that the running binary includes the shutdown fix by
rebuilding or republishing the app:

```powershell
dotnet build .\OnlyRag.sln -c Release
dotnet publish .\src\OnlyRag.App\OnlyRag.App.csproj -c Release -r win-x64 --self-contained true `
  -o .\artifacts\publish\OnlyRag\win-x64 /p:PublishSingleFile=false
```

After confirming the path belongs to OnlyRag, terminate the stale process and relaunch from the
updated output:

```powershell
Stop-Process -Id <ProcessId> -Force
```

For code changes in this area, keep `InProcessBackendHandle.DisposeAsync` independent from the WPF
dispatcher and keep `MainWebView.Dispose()` in the window `Closed` path. Run the API regression tests
before shipping:

```powershell
dotnet test .\tests\OnlyRag.Api.Tests\OnlyRag.Api.Tests.csproj -c Release
```

## Ollama

Ollama must be running locally or reachable on the LAN before using chat, embeddings, or
translation. Configure endpoint and models in **Settings > Ollama**.

Supported operations use the configured Ollama endpoint for model listing, model pull, embeddings,
and chat. The app sends retrieved snippets or unit text as needed; it does not send full documents
for RAG answers.

Loopback endpoints such as `http://localhost:11434` work by default. Any non-loopback endpoint,
including LAN addresses, must be explicitly trusted in Settings before it can be saved or used.

Settings > Ollama stores separate nullable `num_ctx` overrides for chat, embeddings, and document
translation. Automatic mode stores `null` and omits `num_ctx` from Ollama requests; manual mode
persists the selected value and passes it to the relevant generation or embedding request.

## Packaging Status

Windows packaging uses Inno Setup 6 and a per-user install under
`%LOCALAPPDATA%\Programs\OnlyRag`. The publish payload is self-contained for required .NET
runtime components; setup blocks for unsupported Windows builds and missing Microsoft Edge WebView2
Runtime.

```powershell
pwsh .\scripts\Build-Installer.ps1
```

The packaging script builds the React UI, publishes the WPF app as a self-contained `win-x64`
package, validates the required .NET/WebView2/sqlite-vec/OCR payload files, and compiles an
unsigned installer when Inno Setup `ISCC.exe` is installed. For a release candidate, pass
`-SigningCertificateThumbprint` with a trusted code-signing certificate; the build signs with
`signtool`, applies a timestamp, and verifies the signature before returning the artifact. See
[../packaging/README.md](../packaging/README.md) for prerequisites, installer contents, OCR runtime
packaging strategy, and the pre-release checklist.

Packaging is distinct from release. Run `scripts\Test-InstallerRelease.ps1` to create the evidence
artifact required for release verification. Use `-RequireSigned` for signed release candidates.

## Troubleshooting

- Backend is offline in the UI: run the app from PowerShell and inspect `%LOCALAPPDATA%\OnlyRag\logs`.
- Backend is offline immediately after first launch with a `sqlite-vec vec0.dll` message: reinstall
  from a complete installer or rebuild the publish payload, then verify `vec0.dll` exists next to
  `OnlyRag.App.exe`.
- Setup blocks for Windows version: run `winver` and update to Windows 10 version 1809/build 17763
  or newer, or use Windows 11.
- Setup or startup blocks for WebView2: install Microsoft Edge WebView2 Evergreen Runtime from the
  official Microsoft page, then verify it in Settings > Apps or under
  `Program Files\Microsoft\EdgeWebView\Application`.
- Web UI is blank in Debug: run `pwsh .\scripts\Build-Web.ps1`, or start `npm run dev` in
  `src\OnlyRag.Web`.
- Ollama is offline: confirm the endpoint in **Settings > Ollama**, start Ollama, and for LAN
  endpoints configure Ollama network access with `OLLAMA_HOST`.
- Embeddings or chat model missing: install a compatible model from **Settings > Ollama** or with
  Ollama directly.
- Legacy Office import requires LibreOffice: use **Scarica LibreOffice** in Settings, configure
  `soffice.exe`, or set `ONLYRAG_LIBREOFFICE_PATH`.
- OCR reports missing prerequisites: use **Configura OCR** in Settings after Python 3.10+ is
  available, or set `ONLYRAG_OCR_PYTHON` and `ONLYRAG_OCR_BRIDGE`.
- App closes while work is active: confirm the running build includes the WPF shutdown flow and
  inspect `%LOCALAPPDATA%\OnlyRag\logs` for `Shutdown preparation` entries.
- Installer build stops after publish: install Inno Setup 6 or pass `-InnoSetupCompiler` to
  `scripts\Build-Installer.ps1`.

## Project Status

Durable project state and residual work are tracked in `PROJECT_STATUS.json` at the repository
root. Keep completed work out of documentation task lists. Add only real, verifiable residual
documentation problems there when they are not resolved in the current change.
