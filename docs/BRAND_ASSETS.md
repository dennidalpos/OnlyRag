# Asset di Marca

Gli asset grafici vengono generati da [`src/OnlyRag.App/Assets/OnlyRag.svg`](../src/OnlyRag.App/Assets/OnlyRag.svg)
tramite [`scripts/Generate-BrandAssets.ps1`](../scripts/Generate-BrandAssets.ps1).

## Generazione

Eseguire dalla radice del repository su Windows tramite PowerShell 7:

```powershell
pwsh .\scripts\Generate-BrandAssets.ps1
```

## Posizione degli Output

- [`assets/brand/logos`](../assets/brand/logos): Varianti SVG/PNG dell'icona e del logo.
- [`assets/brand/social`](../assets/brand/social): Immagini per i social media e l'anteprima.
- [`assets/brand/setup`](../assets/brand/setup): Grafica per la procedura guidata dell'installer.
- [`src/OnlyRag.Web/public`](../src/OnlyRag.Web/public): Favicon e manifest consumati dalla build web.

