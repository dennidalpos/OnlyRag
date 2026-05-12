#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$CertificateThumbprint,

    [string]$InstallerPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$supportScript = Join-Path $PSScriptRoot "support\BuildSupport.ps1"
. $supportScript

if (-not [string]::IsNullOrWhiteSpace($InstallerPath)) {
    $installerFullPath = [System.IO.Path]::GetFullPath($InstallerPath)
    Assert-OnlyRagPathUnderRepository -RepositoryRoot $repoRoot -Path $installerFullPath
    if (-not (Test-Path -LiteralPath $installerFullPath -PathType Leaf)) {
        throw "Installer not found: $installerFullPath"
    }
}

$normalizedThumbprint = $CertificateThumbprint.Replace(" ", "").ToUpperInvariant()
$checks = [System.Collections.Generic.List[object]]::new()

function Add-TrustCheck {
    param(
        [Parameter(Mandatory)]
        [string]$Id,
        [ValidateSet("pass", "fail", "warn")]
        [string]$Status,
        [Parameter(Mandatory)]
        [string]$Message,
        [object]$Data = $null
    )

    $script:checks.Add([ordered]@{
        id = $Id
        status = $Status
        message = $Message
        data = $Data
    })

    $color = switch ($Status) {
        "pass" { "Green" }
        "warn" { "Yellow" }
        default { "Red" }
    }
    Write-Host "[$Status] $Id - $Message" -ForegroundColor $color
}

function Test-CertificateStore {
    param(
        [Parameter(Mandatory)]
        [string]$Id,
        [Parameter(Mandatory)]
        [string]$StorePath
    )

    $certificatePath = Join-Path $StorePath $normalizedThumbprint
    if (Test-Path -LiteralPath $certificatePath) {
        $certificate = Get-Item -LiteralPath $certificatePath
        Add-TrustCheck -Id $Id -Status "pass" -Message "Certificate found in $StorePath." -Data @{
            subject = $certificate.Subject
            issuer = $certificate.Issuer
            notAfter = $certificate.NotAfter.ToString("O")
        }
        return $true
    }

    Add-TrustCheck -Id $Id -Status "fail" -Message "Certificate missing from $StorePath."
    return $false
}

$rootTrusted = Test-CertificateStore -Id "local-machine-root" -StorePath "Cert:\LocalMachine\Root"
$publisherTrusted = Test-CertificateStore -Id "local-machine-trusted-publisher" -StorePath "Cert:\LocalMachine\TrustedPublisher"

if (-not [string]::IsNullOrWhiteSpace($InstallerPath)) {
    $signature = Get-AuthenticodeSignature -FilePath $installerFullPath
    $signerThumbprint = $signature.SignerCertificate?.Thumbprint?.ToUpperInvariant()
    if ($signerThumbprint -eq $normalizedThumbprint) {
        Add-TrustCheck -Id "installer-signer-thumbprint" -Status "pass" -Message "Installer signer matches expected thumbprint." -Data @{
            status = $signature.Status.ToString()
            statusMessage = $signature.StatusMessage
        }
    }
    else {
        Add-TrustCheck -Id "installer-signer-thumbprint" -Status "fail" -Message "Installer signer does not match expected thumbprint." -Data @{
            expected = $normalizedThumbprint
            actual = $signerThumbprint
            status = $signature.Status.ToString()
            statusMessage = $signature.StatusMessage
        }
    }

    if ($signature.Status -eq "Valid") {
        Add-TrustCheck -Id "installer-signature-trust" -Status "pass" -Message "Installer Authenticode signature is valid."
    }
    else {
        Add-TrustCheck -Id "installer-signature-trust" -Status "fail" -Message "Installer Authenticode signature is not trusted: $($signature.Status)." -Data @{
            statusMessage = $signature.StatusMessage
        }
    }
}
elseif ($rootTrusted -and $publisherTrusted) {
    Add-TrustCheck -Id "installer-signature-trust" -Status "warn" -Message "Trust stores are populated, but no -InstallerPath was supplied for Authenticode verification."
}

$failureCount = @($checks | Where-Object { $_.status -eq "fail" }).Count
if ($failureCount -gt 0) {
    exit 1
}

exit 0
