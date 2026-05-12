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

    [string]$InnoSetupCompiler,

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
$certificateRoot = Join-Path $repoRoot "certificates\app"
$importedThumbprint = $null
$signingThumbprint = $CertificateThumbprint

Assert-OnlyRagPathUnderRepository -RepositoryRoot $repoRoot -Path $outputRootPath

function Get-OnlyRagDefaultCertificatePath {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Certificate directory not found: $Path"
    }

    $certificates = @(Get-ChildItem -LiteralPath $Path -Filter "*.pfx" -File | Sort-Object Name)
    if ($certificates.Count -eq 0) {
        throw "No .pfx certificate found in '$Path'. Pass -CertificatePath or -CertificateThumbprint."
    }
    if ($certificates.Count -gt 1) {
        $names = ($certificates | Select-Object -ExpandProperty Name) -join ", "
        throw "Multiple .pfx certificates found in '$Path': $names. Pass -CertificatePath explicitly."
    }

    return $certificates[0].FullName
}

try {
    if ([string]::IsNullOrWhiteSpace($signingThumbprint)) {
        if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
            $CertificatePath = Get-OnlyRagDefaultCertificatePath -Path $certificateRoot
        }

        if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) {
            throw "Certificate file not found: $CertificatePath"
        }

        $resolvedCertificatePath = (Resolve-Path -LiteralPath $CertificatePath).Path
        $certificateRootFullPath = [System.IO.Path]::GetFullPath($certificateRoot).TrimEnd('\') + '\'
        $certificateFullPath = [System.IO.Path]::GetFullPath($resolvedCertificatePath)
        if (-not $certificateFullPath.StartsWith($certificateRootFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Certificate path must be under '$certificateRoot'."
        }

        if (-not $CertificatePassword) {
            if (-not [string]::IsNullOrWhiteSpace($env:ONLYRAG_CERT_PASSWORD)) {
                $CertificatePassword = ConvertTo-SecureString -String $env:ONLYRAG_CERT_PASSWORD -AsPlainText -Force
            }
            else {
                $CertificatePassword = Read-Host "PFX password" -AsSecureString
            }
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

    if (-not [string]::IsNullOrWhiteSpace($InnoSetupCompiler)) {
        $buildArguments.InnoSetupCompiler = $InnoSetupCompiler
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
