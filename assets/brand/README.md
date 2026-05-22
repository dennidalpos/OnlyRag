# OnlyRag Brand Assets

This directory contains generated visual assets for release, setup, social previews, and post templates.
The assets are derived from `src/OnlyRag.App/Assets/OnlyRag.svg` and can be regenerated on Windows with:

```powershell
pwsh .\scripts\Generate-BrandAssets.ps1
```

## Contents

- `logos/`: SVG logo sources plus PNG icons and wordmark exports. Icon exports are `16`, `32`, `48`, `64`, `128`, `180`, `192`, `256`, `512`, and `1024` px.
- `social/`: Open Graph, GitHub, X/Twitter, LinkedIn, Instagram, stories/reels, and YouTube preview images.
- `setup/`: Inno Setup wizard image and small image BMP/PNG exports.
- `posts/`: product post templates in common square, portrait, and landscape formats.
- `manifest.json`: generated inventory with brand source, output groups, and integration copies.

Package metadata lives with the consuming projects: `src/OnlyRag.App/OnlyRag.App.csproj` for the
Windows executable and `src/OnlyRag.Web/package.json` for the private WebView shell.

## Notes

- Files are generated deterministically by the PowerShell/WPF script; no external image dependency is required.
- The Inno Setup BMP exports match the classic `WizardImageFile` (`164x314`) and `WizardSmallImageFile` (`55x55`) dimensions.
- The generator also updates web favicon/apple/manifest assets under `src\OnlyRag.Web\public`, GitHub-facing images under `.github\assets`, and social metadata images under `src\OnlyRag.Web\public\social`.
- Light/dark asset variants are not generated because the current app has no consuming light/dark
  brand-asset switch.
