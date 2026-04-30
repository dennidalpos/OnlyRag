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

Required for model features:

- Ollama, available locally or on the LAN. Default endpoint: `http://localhost:11434`.

Optional:

- LibreOffice for legacy Office conversion (`.doc`, `.xls`, `.ppt`) and fallback conversion for
  Office files that cannot be read directly.
- Python plus OCR requirements for the PaddleOCR bridge when scanned PDF/image OCR is needed.

## End-user dependency setup

Optional dependencies are configured from **Settings** in the desktop app:

- **Ollama**: when the CLI is missing, the app shows the command
  `irm https://ollama.com/install.ps1 | iex` and can start it in PowerShell.
- **LibreOffice**: when missing, the app opens the official LibreOffice download page.
- **OCR**: **Configura OCR** creates or updates `%LOCALAPPDATA%\OnlyRag\ocr-python\.venv`
  and installs the pinned PaddleOCR requirements shipped with the app.

For Ollama endpoints reachable from another trusted LAN machine, configure Ollama network access
with `OLLAMA_HOST` in the Ollama environment/settings, then restart Ollama and set the endpoint in
**Settings > Ollama**.

## Developer bootstrap

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
| .NET build | `pwsh .\scripts\Build-App.ps1` | Runs `dotnet restore` and `dotnet build` for `OnlyRag.sln`. |
| Web build | `pwsh .\scripts\Build-Web.ps1` | Runs `npm ci` when the lockfile exists, then `npm run build`. |
| Typecheck | `npm run typecheck` from `src\OnlyRag.Web` | Runs TypeScript without emit. |
| Test | `pwsh .\scripts\Test-All.ps1` | Runs `dotnet test` for the solution and web typecheck. |
| Package installer | `pwsh .\scripts\Build-Installer.ps1` | Builds web, publishes WPF, validates publish output, and compiles Inno Setup installer when `ISCC.exe` is installed. Pass `-SigningCertificateThumbprint` for signed release candidates. |
| Verify installer release | `pwsh .\scripts\Test-InstallerRelease.ps1 -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe` | Produces release evidence without installing. Add `-RunInstallLifecycle` on a clean verification machine to test install, shortcuts, launch, upgrade, uninstall, rollback/downgrade, optional components, and signing status. |

No lint script or formatter configuration is currently defined.

The full script inventory and non-canonical script folders are documented in
[`scripts\README.md`](../scripts/README.md). Top-level scripts are reserved for supported setup,
build, test, package, signing, and installer verification flows. Shared helpers live under
`scripts\internal`, and agent/local gates live under `scripts\agents`.

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
| `documents\exports\` | Translation export files. |
| `ocr-python\` | PaddleOCR Python environment prepared by **Configura OCR** in Settings or by developer bootstrap. |
| `logs\` | Application log files. |

The SQLite schema is initialized automatically at startup for a fresh database. OnlyRag is treated
as a new app in this repository: unsupported pre-existing schemas are rejected instead of migrated,
and there is no separate migration command.

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
dotnet publish .\src\OnlyRag.App\OnlyRag.App.csproj -c Release -r win-x64 --self-contained false `
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

Settings > Ollama stores separate nullable `num_ctx` overrides for chat, embeddings, and document
translation. Automatic mode stores `null` and omits `num_ctx` from Ollama requests; manual mode
persists the selected value and passes it to the relevant generation or embedding request.

## Packaging Status

Windows packaging uses Inno Setup 6 and a per-user install under
`%LOCALAPPDATA%\Programs\OnlyRag`.

```powershell
pwsh .\scripts\Build-Installer.ps1
```

The packaging script builds the React UI, publishes the WPF app as a framework-dependent `win-x64`
package, validates the publish payload, and compiles an unsigned installer when Inno Setup
`ISCC.exe` is installed. For a release candidate, pass `-SigningCertificateThumbprint` with a
trusted code-signing certificate; the build signs with `signtool`, applies a timestamp, and verifies
the signature before returning the artifact. See [../packaging/README.md](../packaging/README.md)
for prerequisites, installer contents, OCR runtime packaging strategy, and the pre-release
checklist.

Packaging is distinct from release. Run `scripts\Test-InstallerRelease.ps1` to create the evidence
artifact required for release verification. Use `-RequireSigned` for signed release candidates.

## Troubleshooting

- Backend is offline in the UI: run the app from PowerShell and inspect `%LOCALAPPDATA%\OnlyRag\logs`.
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
