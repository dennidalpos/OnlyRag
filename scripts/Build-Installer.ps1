#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("win-x64")]
    [string]$RuntimeIdentifier = "win-x64",

    [string]$Version = "0.1.0",

    [string]$OutputRoot,

    [string]$InnoSetupCompiler,

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
$wizardImage = Join-Path $repoRoot "assets\brand\setup\onlyrag-setup-wizard-image-164x314.bmp"
$wizardSmallImage = Join-Path $repoRoot "assets\brand\setup\onlyrag-setup-wizard-small-55x55.bmp"
$webRoot = Join-Path $repoRoot "src\OnlyRag.Web"
$innoScript = Join-Path $repoRoot "packaging\OnlyRag.iss"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts"
}

$outputRootPath = [System.IO.Path]::GetFullPath($OutputRoot)
$publishDir = Join-Path $outputRootPath "publish\OnlyRag\$RuntimeIdentifier"
$installerDir = Join-Path $outputRootPath "installer"

Assert-OnlyRagPathUnderRepository -RepositoryRoot $repoRoot -Path $outputRootPath
Assert-OnlyRagPathUnderRepository -RepositoryRoot $repoRoot -Path $publishDir
Assert-OnlyRagPathUnderRepository -RepositoryRoot $repoRoot -Path $installerDir

foreach ($requiredInstallerAsset in @($appIcon, $wizardImage, $wizardSmallImage)) {
    if (-not (Test-Path -LiteralPath $requiredInstallerAsset -PathType Leaf)) {
        throw "Required installer asset was not found: $requiredInstallerAsset"
    }
}

foreach ($directory in @($publishDir, $installerDir)) {
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

$dotnetCommand = Get-Command "dotnet" -ErrorAction SilentlyContinue
if (-not $dotnetCommand) {
    throw "dotnet was not found. Install .NET 10 SDK for Windows."
}

Write-Host "Building React/Vite UI..." -ForegroundColor Cyan
Invoke-OnlyRagWebBuild -WebRoot $webRoot

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

$iscc = Get-OnlyRagInnoSetupCompiler -RequestedPath $InnoSetupCompiler
if (-not $iscc) {
    throw "Inno Setup 6 compiler (ISCC.exe) was not found. dotnet publish completed at '$publishDir', but the installer was not generated."
}

Write-Host "Compiling Inno Setup installer..." -ForegroundColor Cyan
Invoke-OnlyRagNative -FilePath $iscc -WorkingDirectory $repoRoot -Arguments @(
    "/DAppVersion=$Version",
    "/DRuntimeIdentifier=$RuntimeIdentifier",
    "/DPublishDir=$publishDir",
    "/DOutputDir=$installerDir",
    "/DAppIcon=$appIcon",
    "/DWizardImage=$wizardImage",
    "/DWizardSmallImage=$wizardSmallImage",
    $innoScript
)

$installer = Get-ChildItem -LiteralPath $installerDir -Filter "*.exe" -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $installer) {
    throw "Inno Setup completed but no installer executable was found in '$installerDir'."
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
