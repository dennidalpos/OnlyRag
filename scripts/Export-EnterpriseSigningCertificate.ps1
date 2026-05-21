#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$CertificatePath,

    [string]$CertificateThumbprint,

    [string]$InstallerPath,

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

if (-not [string]::IsNullOrWhiteSpace($InstallerPath) -and
    (-not [string]::IsNullOrWhiteSpace($CertificatePath) -or -not [string]::IsNullOrWhiteSpace($CertificateThumbprint))) {
    throw "Pass only one source: -InstallerPath, -CertificatePath, or -CertificateThumbprint."
}

if ([string]::IsNullOrWhiteSpace($InstallerPath) -and
    [string]::IsNullOrWhiteSpace($CertificatePath) -and
    [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    throw "Pass one source: -InstallerPath, -CertificatePath for an external .pfx, or -CertificateThumbprint. Private signing material must not be stored under the repository."
}

if (-not [string]::IsNullOrWhiteSpace($CertificatePath) -and -not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    throw "Pass only one source: -InstallerPath, -CertificatePath, or -CertificateThumbprint."
}

if (-not [string]::IsNullOrWhiteSpace($InstallerPath)) {
    $installerFullPath = [System.IO.Path]::GetFullPath($InstallerPath)
    Assert-OnlyRagPathUnderRepository -RepositoryRoot $repoRoot -Path $installerFullPath
    if (-not (Test-Path -LiteralPath $installerFullPath -PathType Leaf)) {
        throw "Installer not found: $installerFullPath"
    }

    $signature = Get-AuthenticodeSignature -FilePath $installerFullPath
    if (-not $signature.SignerCertificate) {
        throw "Installer does not contain a signer certificate: $installerFullPath"
    }

    $certificate = $signature.SignerCertificate
}
elseif (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
    if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) {
        throw "Certificate file not found: $CertificatePath"
    }

    $resolvedCertificatePath = (Resolve-Path -LiteralPath $CertificatePath).Path
    $repositoryPrefix = [System.IO.Path]::GetFullPath($repoRoot).TrimEnd('\') + '\'
    $certificateFullPath = [System.IO.Path]::GetFullPath($resolvedCertificatePath)
    if ($certificateFullPath.StartsWith($repositoryPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to read a private signing certificate from inside the repository: $resolvedCertificatePath. Move the PFX outside the repository and pass that path."
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
    Write-Warning "The supplied certificate did not expose a private key. Continuing because only the public certificate is exported."
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
