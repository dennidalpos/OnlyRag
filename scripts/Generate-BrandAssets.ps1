#requires -Version 7.0
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$BrandRoot = Join-Path $RepoRoot "assets\brand"
$SourceIconPath = Join-Path $RepoRoot "src\OnlyRag.App\Assets\OnlyRag.svg"

if (-not $IsWindows) {
    throw "Generate-BrandAssets.ps1 requires Windows because it uses WPF imaging assemblies."
}

if (-not (Test-Path -LiteralPath $SourceIconPath -PathType Leaf)) {
    throw "Source icon not found: $SourceIconPath"
}

Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

$Colors = @{
    Ink = "#172033"
    Muted = "#627084"
    Navy = "#123044"
    NavyDark = "#0b1826"
    Blue = "#2d6f8f"
    Teal = "#58d5c9"
    TealDark = "#21a5a2"
    Amber = "#f2c14e"
    Paper = "#ffffff"
    Surface = "#f3f6f8"
    Line = "#d9e0e8"
}

. (Join-Path $PSScriptRoot "support\BrandAssetDrawing.ps1")

$LogoDir = Join-Path $BrandRoot "logos"
$SocialDir = Join-Path $BrandRoot "social"
$SetupDir = Join-Path $BrandRoot "setup"
$PostsDir = Join-Path $BrandRoot "posts"
$WebPublicDir = Join-Path $RepoRoot "src\OnlyRag.Web\public"
$WebSocialDir = Join-Path $WebPublicDir "social"
$GitHubAssetsDir = Join-Path $RepoRoot ".github\assets"
@($LogoDir, $SocialDir, $SetupDir, $PostsDir, $WebPublicDir, $WebSocialDir, $GitHubAssetsDir) | ForEach-Object { New-AssetDirectory $_ }

$sourceIcon = Get-Content -LiteralPath $SourceIconPath -Raw
$iconInner = ($sourceIcon -replace '(?s)^<svg[^>]*>', '' -replace '(?s)</svg>\s*$', '').Trim()
Set-Content -LiteralPath (Join-Path $LogoDir "onlyrag-icon.svg") -Value $sourceIcon -Encoding utf8NoBOM

$horizontalSvg = @"
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 960 320" role="img" aria-label="OnlyRag logo">
  <rect width="960" height="320" fill="#ffffff"/>
  <g transform="translate(64 64) scale(0.75)">
$iconInner
  </g>
  <text x="304" y="150" fill="#172033" font-family="Segoe UI, Arial, sans-serif" font-size="76" font-weight="700">OnlyRag</text>
  <text x="309" y="204" fill="#627084" font-family="Segoe UI, Arial, sans-serif" font-size="28" font-weight="600">Local-first document RAG for Windows</text>
</svg>
"@
Set-Content -LiteralPath (Join-Path $LogoDir "onlyrag-logo-horizontal.svg") -Value $horizontalSvg -Encoding utf8NoBOM

$stackedSvg = @"
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 512" role="img" aria-label="OnlyRag stacked logo">
  <rect width="1024" height="512" fill="#ffffff"/>
  <g transform="translate(384 56) scale(1)">
$iconInner
  </g>
  <text x="512" y="382" fill="#172033" font-family="Segoe UI, Arial, sans-serif" font-size="78" font-weight="700" text-anchor="middle">OnlyRag</text>
  <text x="512" y="430" fill="#627084" font-family="Segoe UI, Arial, sans-serif" font-size="30" font-weight="600" text-anchor="middle">Local document search, chat, OCR, and translation</text>
</svg>
"@
Set-Content -LiteralPath (Join-Path $LogoDir "onlyrag-logo-stacked.svg") -Value $stackedSvg -Encoding utf8NoBOM

$monoSvg = @"
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" role="img" aria-label="OnlyRag monochrome mark">
  <rect width="256" height="256" rx="48" fill="#172033"/>
  <path d="M78 48h68l34 34v88c0 13-8 21-21 21H78c-13 0-21-8-21-21V69c0-13 8-21 21-21Z" fill="#ffffff"/>
  <path d="M145 49v31c0 5 4 9 9 9h27" fill="#d9e0e8"/>
  <path d="M83 104h65M83 128h52M83 152h42" stroke="#172033" stroke-width="10" stroke-linecap="round" opacity="0.58"/>
  <circle cx="157" cy="153" r="34" fill="none" stroke="#172033" stroke-width="16"/>
  <path d="M182 179l25 25" stroke="#172033" stroke-width="18" stroke-linecap="round"/>
</svg>
"@
Set-Content -LiteralPath (Join-Path $LogoDir "onlyrag-mark-mono.svg") -Value $monoSvg -Encoding utf8NoBOM

16, 32, 48, 64, 128, 180, 192, 256, 512, 1024 | ForEach-Object {
    Save-LogoIconPng -Path (Join-Path $LogoDir "onlyrag-icon-$_.png") -Size $_
}
Save-HorizontalLogoPng -Path (Join-Path $LogoDir "onlyrag-logo-horizontal-1200x400.png") -Width 1200 -Height 400
Save-HorizontalLogoPng -Path (Join-Path $LogoDir "onlyrag-logo-horizontal-2400x800.png") -Width 2400 -Height 800
Save-RasterAsset -Path (Join-Path $LogoDir "onlyrag-logo-stacked-1024x512.png") -Width 1024 -Height 512 -Draw {
    param($context, $width, $height)
    $context.DrawRectangle((New-SolidBrush "#ffffff"), $null, [System.Windows.Rect]::new(0, 0, $width, $height))
    Draw-AppIcon -Context $context -X 384 -Y 56 -Size 256
    Draw-Text -Context $context -Text "OnlyRag" -X 0 -Y 342 -Size 78 -Color $Colors.Ink -MaxWidth $width -Weight "Bold"
    Draw-Text -Context $context -Text "Local document search, chat, OCR, and translation" -X 190 -Y 421 -Size 30 -Color $Colors.Muted -MaxWidth 650 -Weight "SemiBold"
}

Save-SocialImage -Path (Join-Path $SocialDir "open-graph-1200x630.png") -Width 1200 -Height 630 -Headline "Local-first document RAG" -Subtitle "Build a private library, retrieve trusted snippets, and chat through Ollama."
Save-SocialImage -Path (Join-Path $SocialDir "github-social-preview-1280x640.png") -Width 1280 -Height 640 -Headline "Private document search for Windows" -Subtitle "OCR, indexing, retrieval, chat, and translation in one local desktop workflow."
Save-SocialImage -Path (Join-Path $SocialDir "x-twitter-card-1600x900.png") -Width 1600 -Height 900 -Headline "RAG over your local documents" -Subtitle "OnlyRag keeps documents and indexes under local app data."
Save-SocialImage -Path (Join-Path $SocialDir "linkedin-share-1200x627.png") -Width 1200 -Height 627 -Headline "Document intelligence, kept local" -Subtitle "A Windows desktop app for teams that need inspectable retrieval and grounded answers."
Save-SocialImage -Path (Join-Path $SocialDir "instagram-square-1080x1080.png") -Width 1080 -Height 1080 -Headline "Search, chat, OCR, translate" -Subtitle "A local-first workflow for document-heavy work."
Save-SocialImage -Path (Join-Path $SocialDir "instagram-portrait-1080x1350.png") -Width 1080 -Height 1350 -Headline "Your document library, searchable locally" -Subtitle "Use Ollama-backed retrieval without sending full documents for RAG answers."
Save-SocialImage -Path (Join-Path $SocialDir "story-reel-1080x1920.png") -Width 1080 -Height 1920 -Headline "Local-first RAG on Windows" -Subtitle "Import documents, index them, ask grounded questions, and export translations."
Save-SocialImage -Path (Join-Path $SocialDir "youtube-thumbnail-1280x720.png") -Width 1280 -Height 720 -Headline "OnlyRag for Windows" -Subtitle "Local document RAG with OCR, Ollama, and translation workflows."

Save-PostImage -Path (Join-Path $PostsDir "post-local-first-rag-1080x1080.png") -Width 1080 -Height 1080 -Label "LOCAL-FIRST RAG" -Headline "Ask grounded questions over your own files" -Subtitle "Documents, indexes, jobs, settings, logs, and exports stay under local app data."
Save-PostImage -Path (Join-Path $PostsDir "post-document-library-1200x1200.png") -Width 1200 -Height 1200 -Label "DOCUMENT LIBRARY" -Headline "Import PDFs, Office files, Markdown, text, and images" -Subtitle "Build a searchable local library with ingestion, OCR, embeddings, and job tracking."
Save-PostImage -Path (Join-Path $PostsDir "post-ollama-ocr-1080x1350.png") -Width 1080 -Height 1350 -Label "OLLAMA + OCR" -Headline "Use local models with scanned and text documents" -Subtitle "Connect to Ollama, run OCR where needed, retrieve source snippets, and keep answers grounded."
Save-PostImage -Path (Join-Path $PostsDir "post-translation-export-1080x1350.png") -Width 1080 -Height 1350 -Label "TRANSLATION" -Headline "Translate indexed documents and export results" -Subtitle "Edit page-based translation units and export TXT, Markdown, HTML, DOCX, or PDF output."
Save-PostImage -Path (Join-Path $PostsDir "post-release-setup-1200x630.png") -Width 1200 -Height 630 -Label "WINDOWS SETUP" -Headline "Installer-ready Windows desktop distribution" -Subtitle "Build, package, sign, and verify release candidates with repository scripts."

Save-RasterAsset -Path (Join-Path $SetupDir "onlyrag-setup-wizard-image-164x314.png") -Width 164 -Height 314 -Draw {
    param($context, $width, $height)
    $context.DrawRectangle((New-LinearBrush $Colors.Navy $Colors.NavyDark 135), $null, [System.Windows.Rect]::new(0, 0, $width, $height))
    Draw-AppIcon -Context $context -X 30 -Y 34 -Size 104
    Draw-Text -Context $context -Text "OnlyRag" -X 18 -Y 168 -Size 26 -Color "#ffffff" -MaxWidth 128 -Weight "Bold"
    Draw-Text -Context $context -Text "Local-first document RAG" -X 18 -Y 206 -Size 13 -Color "#dfe8f0" -MaxWidth 128 -Weight "SemiBold" -MaxHeight 48
    $context.DrawRoundedRectangle((New-SolidBrush $Colors.Amber), $null, [System.Windows.Rect]::new(18, 278, 68, 6), 3, 3)
    $context.DrawRoundedRectangle((New-SolidBrush $Colors.Teal), $null, [System.Windows.Rect]::new(94, 278, 42, 6), 3, 3)
}
Save-RasterAsset -Path (Join-Path $SetupDir "onlyrag-setup-wizard-image-164x314.bmp") -Width 164 -Height 314 -Format Bmp -Draw {
    param($context, $width, $height)
    $context.DrawRectangle((New-LinearBrush $Colors.Navy $Colors.NavyDark 135), $null, [System.Windows.Rect]::new(0, 0, $width, $height))
    Draw-AppIcon -Context $context -X 30 -Y 34 -Size 104
    Draw-Text -Context $context -Text "OnlyRag" -X 18 -Y 168 -Size 26 -Color "#ffffff" -MaxWidth 128 -Weight "Bold"
    Draw-Text -Context $context -Text "Local-first document RAG" -X 18 -Y 206 -Size 13 -Color "#dfe8f0" -MaxWidth 128 -Weight "SemiBold" -MaxHeight 48
    $context.DrawRoundedRectangle((New-SolidBrush $Colors.Amber), $null, [System.Windows.Rect]::new(18, 278, 68, 6), 3, 3)
    $context.DrawRoundedRectangle((New-SolidBrush $Colors.Teal), $null, [System.Windows.Rect]::new(94, 278, 42, 6), 3, 3)
}
Save-RasterAsset -Path (Join-Path $SetupDir "onlyrag-setup-wizard-small-55x55.png") -Width 55 -Height 55 -Draw {
    param($context, $width, $height)
    Draw-AppIcon -Context $context -X 0 -Y 0 -Size 55
}
Save-RasterAsset -Path (Join-Path $SetupDir "onlyrag-setup-wizard-small-55x55.bmp") -Width 55 -Height 55 -Format Bmp -Draw {
    param($context, $width, $height)
    Draw-AppIcon -Context $context -X 0 -Y 0 -Size 55
}
Save-RasterAsset -Path (Join-Path $SetupDir "onlyrag-setup-banner-493x58.png") -Width 493 -Height 58 -Draw {
    param($context, $width, $height)
    $context.DrawRectangle((New-SolidBrush "#ffffff"), $null, [System.Windows.Rect]::new(0, 0, $width, $height))
    Draw-AppIcon -Context $context -X 14 -Y 9 -Size 40
    Draw-Text -Context $context -Text "OnlyRag Setup" -X 68 -Y 10 -Size 20 -Color $Colors.Ink -MaxWidth 240 -Weight "Bold"
    Draw-Text -Context $context -Text "Local-first document RAG for Windows" -X 69 -Y 34 -Size 11 -Color $Colors.Muted -MaxWidth 300 -Weight "SemiBold"
}
Save-RasterAsset -Path (Join-Path $SetupDir "onlyrag-setup-header-1500x500.png") -Width 1500 -Height 500 -Draw {
    param($context, $width, $height)
    Draw-SocialBackground -Context $context -Width $width -Height $height
    Draw-AppIcon -Context $context -X 120 -Y 85 -Size 130
    Draw-Text -Context $context -Text "OnlyRag Setup" -X 285 -Y 105 -Size 62 -Color "#ffffff" -MaxWidth 560 -Weight "Bold"
    Draw-Text -Context $context -Text "Install local-first document search, chat, OCR, and translation workflows." -X 120 -Y 285 -Size 36 -Color $Colors.Ink -MaxWidth 720 -Weight "Bold" -MaxHeight 100
}

$appIconPath = Join-Path $RepoRoot "src\OnlyRag.App\Assets\OnlyRag.ico"
Copy-Item -LiteralPath (Join-Path $LogoDir "onlyrag-icon.svg") -Destination (Join-Path $WebPublicDir "favicon.svg") -Force
Copy-Item -LiteralPath (Join-Path $LogoDir "onlyrag-icon-32.png") -Destination (Join-Path $WebPublicDir "favicon-32x32.png") -Force
Copy-Item -LiteralPath (Join-Path $LogoDir "onlyrag-icon-180.png") -Destination (Join-Path $WebPublicDir "apple-touch-icon.png") -Force
Copy-Item -LiteralPath (Join-Path $LogoDir "onlyrag-icon-192.png") -Destination (Join-Path $WebPublicDir "icon-192.png") -Force
Copy-Item -LiteralPath (Join-Path $LogoDir "onlyrag-icon-512.png") -Destination (Join-Path $WebPublicDir "icon-512.png") -Force
Copy-Item -LiteralPath $appIconPath -Destination (Join-Path $WebPublicDir "favicon.ico") -Force
Copy-Item -LiteralPath (Join-Path $SocialDir "open-graph-1200x630.png") -Destination (Join-Path $WebSocialDir "open-graph-1200x630.png") -Force
Copy-Item -LiteralPath (Join-Path $SocialDir "x-twitter-card-1600x900.png") -Destination (Join-Path $WebSocialDir "x-twitter-card-1600x900.png") -Force
Copy-Item -LiteralPath (Join-Path $LogoDir "onlyrag-icon.svg") -Destination (Join-Path $GitHubAssetsDir "onlyrag-icon.svg") -Force
Copy-Item -LiteralPath (Join-Path $LogoDir "onlyrag-icon-512.png") -Destination (Join-Path $GitHubAssetsDir "onlyrag-icon.png") -Force
Copy-Item -LiteralPath (Join-Path $LogoDir "onlyrag-logo-horizontal-1200x400.png") -Destination (Join-Path $GitHubAssetsDir "onlyrag-logo-horizontal.png") -Force

$webManifest = [ordered]@{
    name = "OnlyRag"
    short_name = "OnlyRag"
    description = "Local-first document RAG for Windows."
    start_url = "."
    display = "standalone"
    background_color = "#f3f6f8"
    theme_color = "#123044"
    icons = @(
        [ordered]@{
            src = "/icon-192.png"
            sizes = "192x192"
            type = "image/png"
            purpose = "any"
        },
        [ordered]@{
            src = "/icon-512.png"
            sizes = "512x512"
            type = "image/png"
            purpose = "any"
        }
    )
}
$webManifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $WebPublicDir "site.webmanifest") -Encoding utf8NoBOM

$manifest = [ordered]@{
    generatedAt = (Get-Date).ToString("s")
    sourceIcon = "src/OnlyRag.App/Assets/OnlyRag.svg"
    outputRoot = "assets/brand"
    categories = [ordered]@{
        app = @(
            "src/OnlyRag.App/Assets/OnlyRag.svg",
            "src/OnlyRag.App/Assets/OnlyRag.ico"
        )
        logos = @(
            "logos/onlyrag-icon.svg",
            "logos/onlyrag-logo-horizontal.svg",
            "logos/onlyrag-logo-stacked.svg",
            "logos/onlyrag-mark-mono.svg",
            "logos/onlyrag-icon-16.png",
            "logos/onlyrag-icon-32.png",
            "logos/onlyrag-icon-48.png",
            "logos/onlyrag-icon-64.png",
            "logos/onlyrag-icon-128.png",
            "logos/onlyrag-icon-180.png",
            "logos/onlyrag-icon-192.png",
            "logos/onlyrag-icon-256.png",
            "logos/onlyrag-icon-512.png",
            "logos/onlyrag-icon-1024.png",
            "logos/onlyrag-logo-horizontal-1200x400.png",
            "logos/onlyrag-logo-horizontal-2400x800.png",
            "logos/onlyrag-logo-stacked-1024x512.png"
        )
        social = @(
            "social/open-graph-1200x630.png",
            "social/github-social-preview-1280x640.png",
            "social/x-twitter-card-1600x900.png",
            "social/linkedin-share-1200x627.png",
            "social/instagram-square-1080x1080.png",
            "social/instagram-portrait-1080x1350.png",
            "social/story-reel-1080x1920.png",
            "social/youtube-thumbnail-1280x720.png"
        )
        setup = @(
            "setup/onlyrag-setup-wizard-image-164x314.png",
            "setup/onlyrag-setup-wizard-image-164x314.bmp",
            "setup/onlyrag-setup-wizard-small-55x55.png",
            "setup/onlyrag-setup-wizard-small-55x55.bmp",
            "setup/onlyrag-setup-banner-493x58.png",
            "setup/onlyrag-setup-header-1500x500.png"
        )
        posts = @(
            "posts/post-local-first-rag-1080x1080.png",
            "posts/post-document-library-1200x1200.png",
            "posts/post-ollama-ocr-1080x1350.png",
            "posts/post-translation-export-1080x1350.png",
            "posts/post-release-setup-1200x630.png"
        )
        integrations = @(
            ".github/assets/onlyrag-icon.svg",
            ".github/assets/onlyrag-icon.png",
            ".github/assets/onlyrag-logo-horizontal.png",
            "src/OnlyRag.Web/public/favicon.ico",
            "src/OnlyRag.Web/public/favicon.svg",
            "src/OnlyRag.Web/public/favicon-32x32.png",
            "src/OnlyRag.Web/public/apple-touch-icon.png",
            "src/OnlyRag.Web/public/icon-192.png",
            "src/OnlyRag.Web/public/icon-512.png",
            "src/OnlyRag.Web/public/site.webmanifest",
            "src/OnlyRag.Web/public/social/open-graph-1200x630.png",
            "src/OnlyRag.Web/public/social/x-twitter-card-1600x900.png"
        )
    }
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $BrandRoot "manifest.json") -Encoding utf8NoBOM

Write-Host "Generated OnlyRag brand assets in $BrandRoot"
