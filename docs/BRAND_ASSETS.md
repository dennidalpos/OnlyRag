# Brand Assets

Brand assets are generated from [`src/OnlyRag.App/Assets/OnlyRag.svg`](../src/OnlyRag.App/Assets/OnlyRag.svg)
by [`scripts/Generate-BrandAssets.ps1`](../scripts/Generate-BrandAssets.ps1).

## Generate

Run from the repository root on Windows with PowerShell 7:

```powershell
pwsh .\scripts\Generate-BrandAssets.ps1
```

The script uses WPF imaging assemblies and therefore requires Windows.

## Output Locations

- [`assets/brand/logos`](../assets/brand/logos): icon and logo SVG/PNG variants.
- [`assets/brand/social`](../assets/brand/social): social preview/card images.
- [`assets/brand/setup`](../assets/brand/setup): installer wizard imagery.
- [`assets/brand/posts`](../assets/brand/posts): prepared post images.
- [`src/OnlyRag.Web/public`](../src/OnlyRag.Web/public): favicon, manifest, and social assets
  consumed by the web UI build.
- [`.github/assets`](../.github/assets): repository image assets used by README/GitHub metadata.

The generated manifest is [`assets/brand/manifest.json`](../assets/brand/manifest.json). Keep it
updated when regenerating assets.

## Packaging Dependencies

The installer script consumes:

- [`src/OnlyRag.App/Assets/OnlyRag.ico`](../src/OnlyRag.App/Assets/OnlyRag.ico)
- [`assets/brand/setup/onlyrag-setup-wizard-image-164x314.bmp`](../assets/brand/setup/onlyrag-setup-wizard-image-164x314.bmp)
- [`assets/brand/setup/onlyrag-setup-wizard-small-55x55.bmp`](../assets/brand/setup/onlyrag-setup-wizard-small-55x55.bmp)
