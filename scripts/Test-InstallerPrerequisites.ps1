#requires -Version 7.0
[CmdletBinding()]
param(
    [switch]$SelfTest,

    [ValidateSet("Present", "Missing")]
    [string]$SimulateWebView2
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Test-OnlyRagWebView2Runtime {
    if ($SimulateWebView2 -eq "Present") {
        return $true
    }
    if ($SimulateWebView2 -eq "Missing") {
        return $false
    }

    $edgeUpdateRoots = @(
        "HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
        "HKCU:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"
    )

    foreach ($root in $edgeUpdateRoots) {
        if (Test-Path -LiteralPath $root) {
            $props = Get-ItemProperty -LiteralPath $root -ErrorAction SilentlyContinue
            if ($props -and -not [string]::IsNullOrWhiteSpace($props.pv)) {
                return $true
            }
        }
    }

    $applicationRoots = @()
    foreach ($basePath in @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:LOCALAPPDATA)) {
        if (-not [string]::IsNullOrWhiteSpace($basePath)) {
            $applicationRoots += Join-Path $basePath "Microsoft\EdgeWebView\Application"
        }
    }

    foreach ($applicationRoot in $applicationRoots) {
        if (Test-Path -LiteralPath $applicationRoot -PathType Container) {
            $runtimeExe = Get-ChildItem -LiteralPath $applicationRoot -Recurse -Filter "msedgewebview2.exe" -File -ErrorAction SilentlyContinue |
                Select-Object -First 1
            if ($runtimeExe) {
                return $true
            }
        }
    }

    return $false
}

function Get-OnlyRagInstallerPrerequisiteStatus {
    param([bool]$WebView2Present)

    if ($WebView2Present) {
        return [pscustomobject]@{
            CanInstall = $true
            Missing = @()
            Message = "All blocking installer prerequisites are present."
        }
    }

    return [pscustomobject]@{
        CanInstall = $false
        Missing = @("Microsoft Edge WebView2 Runtime")
        Message = @"
OnlyRag cannot be installed because a required Windows runtime is missing:

- Software: Microsoft Edge WebView2 Runtime
- Minimum supported version: current Evergreen Runtime for Windows 10 1809 or newer / Windows 11
- Why it is required: OnlyRag is a WPF desktop app that renders its bundled React UI through Microsoft WebView2.
- Install: download and install the official Microsoft Edge WebView2 Evergreen Runtime from https://developer.microsoft.com/microsoft-edge/webview2/
- Verify: open Settings > Apps and confirm "Microsoft Edge WebView2 Runtime" is listed, or check for msedgewebview2.exe under Program Files\Microsoft\EdgeWebView\Application.

After installing WebView2, run this OnlyRag setup again.
"@.Trim()
    }
}

function Assert-Condition {
    param(
        [Parameter(Mandatory)]
        [bool]$Condition,

        [Parameter(Mandatory)]
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

if ($SelfTest) {
    $present = Get-OnlyRagInstallerPrerequisiteStatus -WebView2Present $true
    Assert-Condition -Condition $present.CanInstall -Message "Expected simulated present WebView2 to allow setup."
    Assert-Condition -Condition ($present.Missing.Count -eq 0) -Message "Expected no missing prerequisites for simulated present WebView2."

    $missing = Get-OnlyRagInstallerPrerequisiteStatus -WebView2Present $false
    Assert-Condition -Condition (-not $missing.CanInstall) -Message "Expected simulated missing WebView2 to block setup."
    Assert-Condition -Condition ($missing.Missing -contains "Microsoft Edge WebView2 Runtime") -Message "Expected missing WebView2 prerequisite name."
    foreach ($expected in @("Microsoft Edge WebView2 Runtime", "current Evergreen Runtime", "Why it is required", "Install:", "Verify:")) {
        Assert-Condition -Condition ($missing.Message -like "*$expected*") -Message "Expected installer message to contain '$expected'."
    }

    Write-Host "Installer prerequisite self-test passed." -ForegroundColor Green
    exit 0
}

$webView2Present = Test-OnlyRagWebView2Runtime
$status = Get-OnlyRagInstallerPrerequisiteStatus -WebView2Present $webView2Present

if ($status.CanInstall) {
    Write-Host $status.Message -ForegroundColor Green
    exit 0
}

Write-Host $status.Message -ForegroundColor Red
exit 1
