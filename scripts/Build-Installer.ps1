#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("win-x64")]
    [string]$RuntimeIdentifier = "win-x64",

    [string]$Version = "0.1.0",

    [string]$OutputRoot,

    [string]$NsisCompiler,

    [string]$SigningCertificateThumbprint,

    [string]$TimestampServer = "http://timestamp.digicert.com",

    [string]$SignToolPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$supportScript = Join-Path $PSScriptRoot "support\BuildSupport.ps1"
. $supportScript

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$appProject = Join-Path $repoRoot "src\OnlyRag.App\OnlyRag.App.csproj"
$appIcon = Join-Path $repoRoot "src\OnlyRag.App\Assets\OnlyRag.ico"
$webRoot = Join-Path $repoRoot "src\OnlyRag.Web"
$nsiScript = Join-Path $repoRoot "packaging\OnlyRag.nsi"
$downloadQdrantScript = Join-Path $PSScriptRoot "Download-Qdrant.ps1"
$qdrantExe = Join-Path $repoRoot "packaging\qdrant\payload\qdrant.exe"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts"
}

$outputRootPath = [System.IO.Path]::GetFullPath($OutputRoot)
$publishDir = Join-Path $outputRootPath "publish\OnlyRag\$RuntimeIdentifier"
$installerDir = Join-Path $outputRootPath "installer"

Assert-OnlyRagPathUnderRepository -RepositoryRoot $repoRoot -Path $outputRootPath
Assert-OnlyRagPathUnderRepository -RepositoryRoot $repoRoot -Path $publishDir
Assert-OnlyRagPathUnderRepository -RepositoryRoot $repoRoot -Path $installerDir

if (-not (Test-Path -LiteralPath $appIcon -PathType Leaf)) {
    throw "Required installer asset was not found: $appIcon"
}

foreach ($directory in @($publishDir, $installerDir)) {
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

$dotnetCommand = Assert-OnlyRagDotNetSdk

Write-Host "Building React/Vite UI..." -ForegroundColor Cyan
Invoke-OnlyRagWebBuild -WebRoot $webRoot

if (-not (Test-Path -LiteralPath $qdrantExe -PathType Leaf)) {
    & $downloadQdrantScript
}

Write-Host "Verifying payload binary integrity (SHA256)..." -ForegroundColor Cyan
if (Test-Path -LiteralPath $qdrantExe -PathType Leaf) {
    $qdrantItem = Get-Item -LiteralPath $qdrantExe
    if ($qdrantItem.Length -le 0) {
        throw "Qdrant sidecar binary '$qdrantExe' is empty (0 bytes)."
    }
    $qdrantHash = (Get-FileHash -LiteralPath $qdrantExe -Algorithm SHA256).Hash
    Write-Host "  Qdrant SHA256: $qdrantHash ($($qdrantItem.Length) bytes)" -ForegroundColor Gray
}

$ocrPayloadDir = Join-Path $repoRoot "packaging\ocr\payload"
if (Test-Path -LiteralPath $ocrPayloadDir) {
    Get-ChildItem -LiteralPath $ocrPayloadDir -File -Recurse | ForEach-Object {
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        Write-Host "  OCR Payload ($($_.Name)) SHA256: $hash" -ForegroundColor Gray
    }
}

Write-Host "Publishing OnlyRag WPF app..." -ForegroundColor Cyan
Invoke-OnlyRagNative -FilePath $dotnetCommand.Source -WorkingDirectory $repoRoot -Arguments @(
    "publish",
    $appProject,
    "--configuration",
    $Configuration,
    "--runtime",
    $RuntimeIdentifier,
    "--self-contained",
    "true",
    "--output",
    $publishDir,
    "/p:Version=$Version",
    "/p:PublishSingleFile=false"
)

Test-OnlyRagPublishPayload -Path $publishDir

Write-Host "Auditing binary signatures and generating release installer-manifest.json..." -ForegroundColor Cyan
$manifestFiles = @()
Get-ChildItem -LiteralPath $publishDir -File -Recurse | ForEach-Object {
    $relativePath = [System.IO.Path]::GetRelativePath($publishDir, $_.FullName)
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    $sig = Get-AuthenticodeSignature -FilePath $_.FullName
    $manifestFiles += [ordered]@{
        path = $relativePath
        sizeBytes = $_.Length
        sha256 = $hash
        signatureStatus = $sig.Status.ToString()
        signer = $sig.SignerCertificate?.Subject
    }
}

$manifest = [ordered]@{
    version = $Version
    runtimeIdentifier = $RuntimeIdentifier
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    fileCount = $manifestFiles.Count
    files = $manifestFiles
}

$manifestPath = Join-Path $installerDir "installer-manifest.json"
$manifestJson = $manifest | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($manifestPath, $manifestJson, [System.Text.Encoding]::UTF8)
Write-Host "Installer manifest written: $manifestPath ($($manifestFiles.Count) files audited)" -ForegroundColor Gray

$makensis = Get-OnlyRagNsisCompiler -RequestedPath $NsisCompiler
if (-not $makensis) {
    throw (New-OnlyRagPrerequisiteMessage `
        -Software "NSIS compiler (makensis.exe)" `
        -MinimumVersion "NSIS 3.x" `
        -WhyRequired "OnlyRag uses NSIS to compile the win-x64 Windows installer" `
        -Instruction "Install NSIS from https://nsis.sourceforge.io/ or pass -NsisCompiler with the path to makensis.exe, then rerun the command" `
        -Verify "Run makensis.exe /VERSION or confirm makensis.exe exists under Program Files (x86)\NSIS")
}

Write-Host "Compiling NSIS installer..." -ForegroundColor Cyan
Invoke-OnlyRagNative -FilePath $makensis -WorkingDirectory $repoRoot -Arguments @(
    "/DAPP_VERSION=$Version",
    "/DRUNTIME_IDENTIFIER=$RuntimeIdentifier",
    "/DPUBLISH_DIR=$publishDir",
    "/DOUTPUT_DIR=$installerDir",
    "/DAPP_ICON=$appIcon",
    $nsiScript
)

$installer = Get-ChildItem -LiteralPath $installerDir -Filter "*.exe" -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $installer) {
    throw "NSIS compilation completed but no installer executable was found in '$installerDir'."
}

if (-not [string]::IsNullOrWhiteSpace($SigningCertificateThumbprint)) {
    Invoke-OnlyRagInstallerSigning `
        -InstallerPath $installer.FullName `
        -CertificateThumbprint $SigningCertificateThumbprint `
        -TimestampServer $TimestampServer `
        -SignToolPath $SignToolPath

    $signature = Get-AuthenticodeSignature -FilePath $installer.FullName
    if ($signature.Status -ne "Valid") {
        throw "Installer signature is not valid after signing. Status: $($signature.Status). $($signature.StatusMessage)"
    }
}

Write-Host "Installer artifact: $($installer.FullName)" -ForegroundColor Green
if ([string]::IsNullOrWhiteSpace($SigningCertificateThumbprint)) {
    Write-Host "Build output is packaging only; it is not signed, installed, tested for upgrade/uninstall, deployed, or released." -ForegroundColor Yellow
}
else {
    Write-Host "Installer is signed and locally signature-verified. Run release verification with -RequireSigned before release." -ForegroundColor Yellow
}
