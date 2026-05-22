# Brand Assets

OnlyRag uses a Windows-first product identity based on the existing app icon: document, search lens, local-first desktop workflow, navy background, teal accent, and amber highlight. Do not introduce a separate rebrand unless the app icon and UI tokens change first.

## Source and Generation

Primary editable source:

```text
src\OnlyRag.App\Assets\OnlyRag.svg
```

Regenerate the asset kit from the repository root in PowerShell 7:

```powershell
pwsh .\scripts\Generate-BrandAssets.ps1
```

The script uses WPF drawing APIs available on Windows and does not require external image tooling.
It also refreshes `assets\brand\manifest.json`, which inventories the app icon source/ICO,
generated brand outputs, and copied integration assets.

## Visual Tokens

| Token | Value | Usage |
|---|---|---|
| Ink | `#172033` | Primary text and monochrome mark. |
| Muted | `#627084` | Secondary text. |
| Navy | `#123044` | Primary icon and installer background. |
| Navy dark | `#0b1826` | Icon gradient endpoint. |
| Blue | `#2d6f8f` | Focus, active and selected UI states. |
| Teal | `#58d5c9` | Search lens and accent. |
| Teal dark | `#21a5a2` | Lens gradient endpoint. |
| Amber | `#f2c14e` | Highlight strokes and small accent bars. |
| Surface | `#f3f6f8` | App shell background. |
| Line | `#d9e0e8` | Borders and separators. |

Typography follows the app stack in `src\OnlyRag.Web\src\styles.css`: `"Segoe UI", Inter, system-ui, sans-serif`. Common UI corners use `8px`; the app icon keeps its own `48px` source radius in a `256x256` viewBox.

## Asset Locations

| Asset | Path | Size / format | Consumed by |
|---|---|---|---|
| App icon source | `src\OnlyRag.App\Assets\OnlyRag.svg` | SVG `256x256` viewBox | Brand generator source. |
| App icon | `src\OnlyRag.App\Assets\OnlyRag.ico` | ICO | WPF `ApplicationIcon`, installer icon, web favicon copy. |
| GitHub README logo | `.github\assets\onlyrag-logo-horizontal.png` | PNG `1200x400` | Root `README.md`. |
| GitHub icon | `.github\assets\onlyrag-icon.svg`, `.github\assets\onlyrag-icon.png` | SVG, PNG `512x512` | Repository media and fallback docs. |
| Logo sources | `assets\brand\logos\*.svg` | SVG | Editable brand kit outputs. |
| Logo raster exports | `assets\brand\logos\*.png` | PNG `16` through `1024`, plus wordmarks | Docs, package pages, manual release media. |
| Web favicon | `src\OnlyRag.Web\public\favicon.ico`, `favicon.svg`, `favicon-32x32.png` | ICO, SVG, PNG `32x32` | `src\OnlyRag.Web\index.html`, packaged `wwwroot`. |
| Apple touch icon | `src\OnlyRag.Web\public\apple-touch-icon.png` | PNG `180x180` | `src\OnlyRag.Web\index.html`. |
| Web manifest icons | `src\OnlyRag.Web\public\icon-192.png`, `icon-512.png`, `site.webmanifest` | PNG `192x192`, `512x512`, JSON with `purpose: any` | Browser/webview metadata. |
| Web social metadata images | `src\OnlyRag.Web\public\social\open-graph-1200x630.png`, `x-twitter-card-1600x900.png` | PNG | Open Graph and X/Twitter tags in `index.html`. |
| Source social kit | `assets\brand\social\*.png` | PNG provider sizes | Manual release/social metadata use. |
| Source post kit | `assets\brand\posts\*.png` | PNG square/portrait/landscape | Announcement drafts and package listings. |
| Installer wizard image | `assets\brand\setup\onlyrag-setup-wizard-image-164x314.bmp` | BMP `164x314` | `packaging\OnlyRag.iss` `WizardImageFile`. |
| Installer wizard small image | `assets\brand\setup\onlyrag-setup-wizard-small-55x55.bmp` | BMP `55x55` | `packaging\OnlyRag.iss` `WizardSmallImageFile`. |
| App package metadata | `src\OnlyRag.App\OnlyRag.App.csproj` | MSBuild properties | Windows executable product, company, version, description, and project URL metadata. |
| Web package metadata | `src\OnlyRag.Web\package.json` | npm package metadata | Private WebView shell package name, version, description, and homepage. |
| Payhip listing copy | `assets\payhip\payhip-listing-it.md` | Markdown | Italian listing draft and screenshot ordering for the current Payhip asset folder. |
| Payhip screenshots | `assets\payhip\Screenshot_1.png`, `Screenshot_2.png`, `Screenshot_3.png` | PNG | Current product listing screenshots. |

OnlyRag does not currently expose light/dark brand-asset switching. The editable SVG and generated
rasters are single-theme assets matched to the current app icon, installer artwork, and light UI.
Add explicit light/dark exports only when the app has a consuming surface for them.

## Naming

Asset names are lowercase and hyphenated with the `onlyrag-` prefix. Raster names include their purpose and dimensions when multiple sizes exist, for example `onlyrag-icon-512.png` or `open-graph-1200x630.png`.

## Verification

Run the repository gate after asset changes:

```powershell
pwsh .\scripts\Invoke-Gate.ps1 -Configuration Release
```

For installer artwork changes, also run packaging on a machine with Inno Setup 6:

```powershell
pwsh .\scripts\Build-Installer.ps1 -Configuration Release
```

Before committing asset path changes, verify that referenced files exist:

```powershell
@(
  ".github\assets\onlyrag-logo-horizontal.png",
  ".github\assets\onlyrag-icon.svg",
  ".github\assets\onlyrag-icon.png",
  "src\OnlyRag.App\Assets\OnlyRag.ico",
  "src\OnlyRag.App\Assets\OnlyRag.svg",
  "src\OnlyRag.Web\public\favicon.ico",
  "src\OnlyRag.Web\public\favicon.svg",
  "src\OnlyRag.Web\public\favicon-32x32.png",
  "src\OnlyRag.Web\public\apple-touch-icon.png",
  "src\OnlyRag.Web\public\icon-192.png",
  "src\OnlyRag.Web\public\icon-512.png",
  "src\OnlyRag.Web\public\site.webmanifest",
  "src\OnlyRag.Web\public\social\open-graph-1200x630.png",
  "src\OnlyRag.Web\public\social\x-twitter-card-1600x900.png",
  "assets\brand\setup\onlyrag-setup-wizard-image-164x314.bmp",
  "assets\brand\setup\onlyrag-setup-wizard-small-55x55.bmp"
) | ForEach-Object {
  if (-not (Test-Path -LiteralPath $_ -PathType Leaf)) { throw "Missing asset: $_" }
}
```
