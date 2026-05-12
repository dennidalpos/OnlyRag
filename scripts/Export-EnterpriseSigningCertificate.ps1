#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$CertificatePath,

    [string]$CertificateThumbprint,

    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$supportScript = Join-Path $PSScriptRoot "support\BuildSupport.ps1"
. $supportScript

$certificateRoot = Join-Path $repoRoot "certificates\app"
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $certificateRoot "OnlyRag-Enterprise-CodeSigning.cer"
}

$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
Assert-OnlyRagPathUnderRepository -RepositoryRoot $repoRoot -Path $outputFullPath

if ([string]::IsNullOrWhiteSpace($CertificatePath) -and [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    $certificates = @(Get-ChildItem -LiteralPath $certificateRoot -Filter "*.pfx" -File | Sort-Object Name)
    if ($certificates.Count -eq 0) {
        throw "No .pfx certificate found in '$certificateRoot'. Pass -CertificatePath or -CertificateThumbprint."
    }
    if ($certificates.Count -gt 1) {
        $names = ($certificates | Select-Object -ExpandProperty Name) -join ", "
        throw "Multiple .pfx certificates found in '$certificateRoot': $names. Pass -CertificatePath explicitly."
    }

    $CertificatePath = $certificates[0].FullName
}

if (-not [string]::IsNullOrWhiteSpace($CertificatePath) -and -not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    throw "Pass either -CertificatePath or -CertificateThumbprint, not both."
}

if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
    if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) {
        throw "Certificate file not found: $CertificatePath"
    }

    $resolvedCertificatePath = (Resolve-Path -LiteralPath $CertificatePath).Path
    $certificateRootFullPath = [System.IO.Path]::GetFullPath($certificateRoot).TrimEnd('\') + '\'
    $certificateFullPath = [System.IO.Path]::GetFullPath($resolvedCertificatePath)
    if (-not $certificateFullPath.StartsWith($certificateRootFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Certificate path must be under '$certificateRoot'."
    }

    $certificate = Get-PfxCertificate -FilePath $resolvedCertificatePath
}
else {
    $normalizedThumbprint = $CertificateThumbprint.Replace(" ", "")
    $candidatePaths = @(
        "Cert:\CurrentUser\My\$normalizedThumbprint",
        "Cert:\LocalMachine\My\$normalizedThumbprint",
        "Cert:\CurrentUser\Root\$normalizedThumbprint",
        "Cert:\LocalMachine\Root\$normalizedThumbprint",
        "Cert:\CurrentUser\TrustedPublisher\$normalizedThumbprint",
        "Cert:\LocalMachine\TrustedPublisher\$normalizedThumbprint"
    )

    $certificate = $null
    foreach ($candidatePath in $candidatePaths) {
        if (Test-Path -LiteralPath $candidatePath) {
            $certificate = Get-Item -LiteralPath $candidatePath
            break
        }
    }

    if (-not $certificate) {
        throw "Certificate thumbprint was not found in common code-signing or trust stores: $normalizedThumbprint"
    }
}

if (-not $certificate.HasPrivateKey -and -not [string]::IsNullOrWhiteSpace($CertificatePath)) {
    throw "The supplied PFX did not expose a private key. Confirm this is the signing certificate."
}

$outputDirectory = Split-Path -Parent $outputFullPath
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$exported = Export-Certificate -Cert $certificate -FilePath $outputFullPath -Force
if (-not $exported -or -not (Test-Path -LiteralPath $outputFullPath -PathType Leaf)) {
    throw "Certificate export failed: $outputFullPath"
}

Write-Host "Exported public certificate: $outputFullPath" -ForegroundColor Green
Write-Host "Thumbprint: $($certificate.Thumbprint)" -ForegroundColor Cyan
Write-Host "Subject: $($certificate.Subject)" -ForegroundColor Cyan
Write-Host "Issuer: $($certificate.Issuer)" -ForegroundColor Cyan
Write-Host "NotAfter: $($certificate.NotAfter.ToString('O'))" -ForegroundColor Cyan
Write-Host ""
Write-Host "Deploy this .cer through Group Policy to:" -ForegroundColor Yellow
Write-Host "  - Computer Configuration > Policies > Windows Settings > Security Settings > Public Key Policies > Trusted Root Certification Authorities"
Write-Host "  - Computer Configuration > Policies > Windows Settings > Security Settings > Public Key Policies > Trusted Publishers"
