#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$ManifestPath = "",

    [string]$OutputDirectory = "",

    [switch]$Force
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $repoRoot "packaging\qdrant\manifest.json"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "packaging\qdrant\payload"
}

if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "Qdrant manifest not found: $ManifestPath"
}

$manifest = Get-Content -Raw -LiteralPath $ManifestPath | ConvertFrom-Json
$qdrantExe = Join-Path $OutputDirectory "qdrant.exe"
$licensePath = Join-Path $OutputDirectory "LICENSE"
if (-not $Force -and (Test-Path -LiteralPath $qdrantExe -PathType Leaf) -and (Test-Path -LiteralPath $licensePath -PathType Leaf)) {
    Write-Host "Qdrant payload already prepared at $OutputDirectory"
    return
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$downloadPath = Join-Path ([System.IO.Path]::GetTempPath()) $manifest.assetName
$licenseDownloadPath = Join-Path ([System.IO.Path]::GetTempPath()) "onlyrag-qdrant-LICENSE-$($manifest.version)"
$extractPath = Join-Path ([System.IO.Path]::GetTempPath()) ("onlyrag-qdrant-" + [Guid]::NewGuid().ToString("N"))

try {
    Invoke-WebRequest -Uri $manifest.downloadUrl -OutFile $downloadPath
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $downloadPath).Hash
    if (-not [string]::Equals($actualHash, $manifest.sha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Qdrant SHA256 mismatch. Expected $($manifest.sha256), got $actualHash."
    }

    Expand-Archive -LiteralPath $downloadPath -DestinationPath $extractPath -Force
    $extractedExe = Get-ChildItem -LiteralPath $extractPath -Recurse -Filter "qdrant.exe" | Select-Object -First 1
    if ($null -eq $extractedExe) {
        throw "qdrant.exe was not found inside $($manifest.assetName)."
    }

    Copy-Item -LiteralPath $extractedExe.FullName -Destination $qdrantExe -Force
    if ($manifest.PSObject.Properties.Name -contains "licenseUrl") {
        Invoke-WebRequest -Uri $manifest.licenseUrl -OutFile $licenseDownloadPath
        if ($manifest.PSObject.Properties.Name -contains "licenseSha256") {
            $actualLicenseHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $licenseDownloadPath).Hash
            if (-not [string]::Equals($actualLicenseHash, $manifest.licenseSha256, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Qdrant LICENSE SHA256 mismatch. Expected $($manifest.licenseSha256), got $actualLicenseHash."
            }
        }

        Copy-Item -LiteralPath $licenseDownloadPath -Destination $licensePath -Force
    }
    else {
        $extractedLicense = Get-ChildItem -LiteralPath $extractPath -Recurse -Filter "LICENSE" | Select-Object -First 1
        if ($null -eq $extractedLicense) {
            throw "Qdrant LICENSE was not found inside $($manifest.assetName)."
        }

        Copy-Item -LiteralPath $extractedLicense.FullName -Destination $licensePath -Force
    }
    Copy-Item -LiteralPath $ManifestPath -Destination (Join-Path $OutputDirectory "manifest.json") -Force
    Write-Host "Qdrant $($manifest.version) prepared at $OutputDirectory"
}
finally {
    if (Test-Path -LiteralPath $downloadPath -PathType Leaf) {
        Remove-Item -LiteralPath $downloadPath -Force
    }

    if (Test-Path -LiteralPath $licenseDownloadPath -PathType Leaf) {
        Remove-Item -LiteralPath $licenseDownloadPath -Force
    }

    if (Test-Path -LiteralPath $extractPath -PathType Container) {
        Remove-Item -LiteralPath $extractPath -Recurse -Force
    }
}
