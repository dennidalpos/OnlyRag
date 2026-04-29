# OnlyRag brand assets

This directory contains generated visual assets for release, setup, social previews, and post templates.
The assets are derived from `src/OnlyRag.App/Assets/OnlyRag.svg` and can be regenerated on Windows with:

```powershell
pwsh .\assets\brand\Generate-BrandAssets.ps1
```

## Contents

- `logos/`: SVG logo sources plus PNG icons and wordmark exports.
- `social/`: common provider preview sizes, including Open Graph, GitHub, X/Twitter, LinkedIn, Instagram, stories/reels, and YouTube.
- `setup/`: Windows installer-oriented artwork, including Inno Setup wizard image and small image BMP/PNG exports.
- `posts/`: ready-to-use product post templates in common square, portrait, and landscape formats.
- `manifest.json`: generated inventory with relative file paths.

## Notes

- Files are generated deterministically by the PowerShell/WPF script; no external image dependency is required.
- The Inno Setup BMP exports match the classic `WizardImageFile` (`164x314`) and `WizardSmallImageFile` (`55x55`) dimensions.
- The current installer script still uses the application icon only. Wire the setup artwork into `packaging/OnlyRag.iss` only when the release packaging scope requires it.
