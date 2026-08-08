# Packaging dell'Installer

OnlyRag utilizza NSIS (Nullsoft Scriptable Install System) su Windows per impacchettare l'applicazione desktop autotenuta per `win-x64`.

## Input

- [`OnlyRag.nsi`](OnlyRag.nsi): Script dell'installer NSIS.
- [`../src/OnlyRag.App/OnlyRag.App.csproj`](../src/OnlyRag.App/OnlyRag.App.csproj): Progetto WPF di output.
- [`../src/OnlyRag.Web/dist/index.html`](../src/OnlyRag.Web/dist/index.html): Build della UI web.
- [`qdrant/manifest.json`](qdrant/manifest.json): Metadati del runtime Qdrant integrato.
- [`qdrant/payload/qdrant.exe`](qdrant/payload/qdrant.exe): Eseguibile di Qdrant scaricato da [`../scripts/Download-Qdrant.ps1`](../scripts/Download-Qdrant.ps1).

## Compilazione

Installer non firmato:

```powershell
pwsh .\scripts\Build-Installer.ps1 -Configuration Release
```

Installer firmato:

```powershell
pwsh .\scripts\Sign-Release.ps1 -CertificateThumbprint <thumbprint>
```

Output generati:

- `artifacts\publish\OnlyRag\win-x64`
- `artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe`

## Comportamento dell'Installer

- Destinazione 64-bit Windows (`win-x64`).
- Percorso di installazione: `%LOCALAPPDATA%\Programs\OnlyRag`.
- Conserva i dati utente in `%LOCALAPPDATA%\OnlyRag` alla disinstallazione.
- Include il runtime .NET autotenuto, WebView2, SQLite, ONNX/DirectML, Qdrant, script OCR e UI web.

