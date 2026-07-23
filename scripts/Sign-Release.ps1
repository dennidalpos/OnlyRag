#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("win-x64")]
    [string]$RuntimeIdentifier = "win-x64",

    [string]$Version = "0.1.0",

    [string]$OutputRoot,

    [string]$CertificatePath,

    [securestring]$CertificatePassword,

    [string]$CertificateThumbprint,

    [switch]$KeepImportedCertificate,

    [string]$TimestampServer = "http://timestamp.digicert.com",

    [string]$NsisCompiler,

    [string]$SignToolPath,

    [switch]$SkipReleaseVerification
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$supportScript = Join-Path $PSScriptRoot "support\BuildSupport.ps1"
. $supportScript

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts"
}

$outputRootPath = [System.IO.Path]::GetFullPath($OutputRoot)
$importedThumbprint = $null
$signingThumbprint = $CertificateThumbprint

Assert-OnlyRagPathUnderRepository -RepositoryRoot $repoRoot -Path $outputRootPath

function Test-OnlyRagPathInRepository {
    param([Parameter(Mandatory)][string]$Path)

    $repositoryPrefix = [System.IO.Path]::GetFullPath($repoRoot).TrimEnd('\') + '\'
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    return $fullPath.StartsWith($repositoryPrefix, [System.StringComparison]::OrdinalIgnoreCase)
}

try {
    if ([string]::IsNullOrWhiteSpace($signingThumbprint)) {
        if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
            throw "Pass -CertificatePath for an external .pfx file or -CertificateThumbprint for an installed certificate. Private signing material must not be stored under the repository."
        }

        if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) {
            throw "Certificate file not found: $CertificatePath"
        }

        $resolvedCertificatePath = (Resolve-Path -LiteralPath $CertificatePath).Path
        if (Test-OnlyRagPathInRepository -Path $resolvedCertificatePath) {
            throw "Refusing to import a private signing certificate from inside the repository: $resolvedCertificatePath. Move the PFX outside the repository and pass that path."
        }

        if (-not $CertificatePassword) {
            $CertificatePassword = Read-Host "PFX password" -AsSecureString
        }

        Write-Host "Importing signing certificate into CurrentUser\\My..." -ForegroundColor Cyan
        $importedCertificate = Import-PfxCertificate `
            -FilePath $resolvedCertificatePath `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -Password $CertificatePassword `
            -Exportable:$false

        if (-not $importedCertificate -or [string]::IsNullOrWhiteSpace($importedCertificate.Thumbprint)) {
            throw "Certificate import completed without a usable thumbprint."
        }

        $importedThumbprint = $importedCertificate.Thumbprint.Replace(" ", "")
        $signingThumbprint = $importedThumbprint
    }
    else {
        $signingThumbprint = $signingThumbprint.Replace(" ", "")
    }

    $buildScript = Join-Path $PSScriptRoot "Build-Installer.ps1"
    $buildArguments = @{
        Configuration = $Configuration
        RuntimeIdentifier = $RuntimeIdentifier
        Version = $Version
        OutputRoot = $outputRootPath
        SigningCertificateThumbprint = $signingThumbprint
        TimestampServer = $TimestampServer
    }

    if (-not [string]::IsNullOrWhiteSpace($NsisCompiler)) {
        $buildArguments.NsisCompiler = $NsisCompiler
    }
    if (-not [string]::IsNullOrWhiteSpace($SignToolPath)) {
        $buildArguments.SignToolPath = $SignToolPath
    }

    Write-Host "Building and signing OnlyRag installer..." -ForegroundColor Cyan
    & $buildScript @buildArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Build-Installer.ps1 failed with exit code $LASTEXITCODE."
    }

    $installerDir = Join-Path $outputRootPath "installer"
    $installer = Get-ChildItem -LiteralPath $installerDir -Filter "*.exe" -File |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not $installer) {
        throw "Signed installer was not found in '$installerDir'."
    }

    if (-not $SkipReleaseVerification) {
        $verificationScript = Join-Path $PSScriptRoot "Test-InstallerRelease.ps1"
        Write-Host "Running signed release verification..." -ForegroundColor Cyan
        & $verificationScript -InstallerPath $installer.FullName -RequireSigned
        if ($LASTEXITCODE -ne 0) {
            throw "Signed release verification failed with exit code $LASTEXITCODE."
        }
    }

    Write-Host "Signed installer artifact: $($installer.FullName)" -ForegroundColor Green
}
finally {
    if ($importedThumbprint -and -not $KeepImportedCertificate) {
        $storePath = "Cert:\CurrentUser\My\$importedThumbprint"
        if (Test-Path -LiteralPath $storePath) {
            Write-Host "Removing temporarily imported certificate from CurrentUser\\My..." -ForegroundColor Cyan
            Remove-Item -LiteralPath $storePath -Force
        }
    }
}
