#requires -Version 7.0
[CmdletBinding()]
param(
    [switch]$SelfTest,

    [ValidateSet("Present", "Missing")]
    [string]$SimulateWebView2,

    [ValidateSet("Supported", "Unsupported")]
    [string]$SimulateWindows
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Test-OnlyRagSupportedWindows {
    param(
        [ValidateSet("", "Supported", "Unsupported")]
        [string]$Simulation
    )

    if ($Simulation -eq "Supported") {
        return $true
    }
    if ($Simulation -eq "Unsupported") {
        return $false
    }

    if (-not $IsWindows) {
        return $false
    }

    $version = [System.Environment]::OSVersion.Version
    return (
        $version.Major -gt 10 -or
        ($version.Major -eq 10 -and $version.Minor -gt 0) -or
        ($version.Major -eq 10 -and $version.Minor -eq 0 -and $version.Build -ge 17763)
    )
}

function Test-OnlyRagWebView2Runtime {
    param(
        [ValidateSet("", "Present", "Missing")]
        [string]$Simulation
    )

    if ($Simulation -eq "Present") {
        return $true
    }
    if ($Simulation -eq "Missing") {
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
    param(
        [bool]$WindowsSupported,
        [bool]$WebView2Present,
        [string]$AppName = "OnlyRag"
    )

    $maxInstallerMessageLineLength = 86

    function Join-OnlyRagWrappedLine {
        param(
            [string]$Prefix,

            [Parameter(Mandatory)]
            [string]$Text
        )

        $result = [System.Collections.Generic.List[string]]::new()
        $currentLine = ""
        $continuationPrefix = " " * $Prefix.Length

        foreach ($word in ($Text -split "\s+")) {
            if ([string]::IsNullOrWhiteSpace($word)) {
                continue
            }

            if ([string]::IsNullOrWhiteSpace($currentLine)) {
                $currentLine = "$Prefix$word"
                continue
            }

            if (($currentLine.Length + 1 + $word.Length) -le $maxInstallerMessageLineLength) {
                $currentLine = "$currentLine $word"
                continue
            }

            $result.Add($currentLine)
            $currentLine = "$continuationPrefix$word"
        }

        if (-not [string]::IsNullOrWhiteSpace($currentLine)) {
            $result.Add($currentLine)
        }

        return ($result -join [Environment]::NewLine)
    }

    function New-OnlyRagInstallerBullet {
        param(
            [Parameter(Mandatory)]
            [string]$Label,

            [Parameter(Mandatory)]
            [string]$Text
        )

        return Join-OnlyRagWrappedLine -Prefix "- ${Label}: " -Text $Text
    }

    function New-OnlyRagInstallerParagraph {
        param([Parameter(Mandatory)][string]$Text)

        return Join-OnlyRagWrappedLine -Prefix "" -Text $Text
    }

    if (-not $WindowsSupported) {
        $message = @(
            New-OnlyRagInstallerParagraph "$AppName cannot be installed because this Windows version is not supported."
            ""
            New-OnlyRagInstallerBullet "Software" "Microsoft Windows"
            New-OnlyRagInstallerBullet "Minimum supported version" "Windows 10 version 1809, build 17763, or Windows 11"
            New-OnlyRagInstallerBullet "Why it is required" "$AppName is a modern Windows desktop app using WPF, WebView2, and a self-contained .NET Windows runtime payload validated for Windows 10 1809 or newer"
            New-OnlyRagInstallerBullet "Install" "Update Windows through Settings > Windows Update, or use a Windows 10/11 client that meets the minimum version"
            New-OnlyRagInstallerBullet "Verify" "Press Win+R, run winver, and confirm Windows 10 version 1809/build 17763 or newer, or Windows 11"
            ""
            New-OnlyRagInstallerParagraph "After updating Windows, run this $AppName setup again."
        ) -join [Environment]::NewLine

        return [pscustomobject]@{
            CanInstall = $false
            Missing = @("Microsoft Windows 10 version 1809/build 17763 or newer")
            Message = $message
        }
    }

    if ($WebView2Present) {
        return [pscustomobject]@{
            CanInstall = $true
            Missing = @()
            Message = "All blocking installer prerequisites are present."
        }
    }

    $message = @(
        New-OnlyRagInstallerParagraph "$AppName cannot be installed because a required Windows runtime is missing."
        ""
        New-OnlyRagInstallerBullet "Software" "Microsoft Edge WebView2 Runtime"
        New-OnlyRagInstallerBullet "Minimum supported version" "Current Evergreen Runtime for supported Windows versions"
        New-OnlyRagInstallerBullet "Why it is required" "$AppName renders its bundled React UI through Microsoft WebView2"
        New-OnlyRagInstallerBullet "Install" "Download and install the official Microsoft Edge WebView2 Evergreen Runtime from https://developer.microsoft.com/microsoft-edge/webview2/"
        New-OnlyRagInstallerBullet "Verify" "Open Settings > Apps and confirm Microsoft Edge WebView2 Runtime is listed, or check for msedgewebview2.exe under Program Files\Microsoft\EdgeWebView\Application"
        ""
        New-OnlyRagInstallerParagraph "After installing WebView2, run this $AppName setup again."
        ""
        New-OnlyRagInstallerParagraph "The installer includes the required .NET runtime components and OCR CPU/NVIDIA provisioning manifests. Setup automatically prepares PaddleOCR packages when compatible Python and Internet access are available. Ollama and LibreOffice remain user-confirmed external/manual installs."
    ) -join [Environment]::NewLine

    return [pscustomobject]@{
        CanInstall = $false
        Missing = @("Microsoft Edge WebView2 Runtime")
        Message = $message
    }
}

function Get-OnlyRagNvidiaGpuOcrMemo {
    param([bool]$NvidiaToolsPresent)

    if ($NvidiaToolsPresent) {
        return @(
            "- NVIDIA OCR: NVIDIA management tools were detected. Setup will try the"
            "              compatible GPU runtime automatically and OnlyRag will select GPU"
            "              after Diagnostics reports it usable, unless CPU was saved manually."
        ) -join [Environment]::NewLine
    }

    return @(
        "- NVIDIA OCR: NVIDIA management tools were not detected. OCR provisioning will"
        "              use the CPU runtime unless a compatible NVIDIA driver is"
        "              installed later."
    ) -join [Environment]::NewLine
}

function Test-OnlyRagNvidiaManagementTools {
    $systemCandidate = Join-Path $env:WINDIR "System32\nvidia-smi.exe"
    if (Test-Path -LiteralPath $systemCandidate -PathType Leaf) {
        return $true
    }

    return [bool](Get-Command "nvidia-smi" -ErrorAction SilentlyContinue)
}

function Assert-InstallerMessageLayout {
    param(
        [Parameter(Mandatory)]
        [string]$Message,

        [Parameter(Mandatory)]
        [string]$Scenario
    )

    $maxInstallerMessageLineLength = 86
    $longLines = @($Message -split "\r?\n" | Where-Object { $_.Length -gt $maxInstallerMessageLineLength })
    Assert-Condition -Condition ($longLines.Count -eq 0) -Message "Expected $Scenario installer message lines to be no longer than $maxInstallerMessageLineLength characters. Long lines: $($longLines -join ' | ')"
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
    $buildSupportScript = Join-Path $PSScriptRoot "support\BuildSupport.ps1"
    . $buildSupportScript

    $present = Get-OnlyRagInstallerPrerequisiteStatus -WindowsSupported $true -WebView2Present $true
    Assert-Condition -Condition $present.CanInstall -Message "Expected simulated present WebView2 to allow setup."
    Assert-Condition -Condition ($present.Missing.Count -eq 0) -Message "Expected no missing prerequisites for simulated present WebView2."

    $missing = Get-OnlyRagInstallerPrerequisiteStatus -WindowsSupported $true -WebView2Present $false
    Assert-Condition -Condition (-not $missing.CanInstall) -Message "Expected simulated missing WebView2 to block setup."
    Assert-Condition -Condition ($missing.Missing -contains "Microsoft Edge WebView2 Runtime") -Message "Expected missing WebView2 prerequisite name."
    foreach ($expected in @("Microsoft Edge WebView2 Runtime", "current Evergreen Runtime", "Why it is required", "Install:", "Verify:")) {
        Assert-Condition -Condition ($missing.Message -like "*$expected*") -Message "Expected installer message to contain '$expected'."
    }
    Assert-InstallerMessageLayout -Message $missing.Message -Scenario "missing WebView2"
    Assert-Condition -Condition ($missing.Message -like "*OCR CPU/NVIDIA*") -Message "Expected WebView2 message to mention OCR NVIDIA."
    Assert-Condition -Condition ($missing.Message -like "*provisioning manifests*") -Message "Expected WebView2 message to mention provisioning manifests."

    $nvidiaPresentMemo = Get-OnlyRagNvidiaGpuOcrMemo -NvidiaToolsPresent $true
    Assert-Condition -Condition ($nvidiaPresentMemo -like "*NVIDIA management tools were detected*") -Message "Expected NVIDIA-present memo."
    Assert-InstallerMessageLayout -Message $nvidiaPresentMemo -Scenario "NVIDIA present memo"

    $nvidiaMissingMemo = Get-OnlyRagNvidiaGpuOcrMemo -NvidiaToolsPresent $false
    Assert-Condition -Condition ($nvidiaMissingMemo -like "*CPU runtime*") -Message "Expected NVIDIA-missing CPU fallback memo."
    Assert-InstallerMessageLayout -Message $nvidiaMissingMemo -Scenario "NVIDIA missing memo"

    $unsupportedWindows = Get-OnlyRagInstallerPrerequisiteStatus -WindowsSupported $false -WebView2Present $true
    Assert-Condition -Condition (-not $unsupportedWindows.CanInstall) -Message "Expected simulated unsupported Windows to block setup."
    Assert-Condition -Condition ($unsupportedWindows.Missing -contains "Microsoft Windows 10 version 1809/build 17763 or newer") -Message "Expected missing Windows prerequisite name."
    foreach ($expected in @("Microsoft Windows", "Windows 10 version 1809", "Why it is required", "Windows Update", "winver")) {
        Assert-Condition -Condition ($unsupportedWindows.Message -like "*$expected*") -Message "Expected Windows message to contain '$expected'."
    }
    Assert-InstallerMessageLayout -Message $unsupportedWindows.Message -Scenario "unsupported Windows"

    $longProductName = "OnlyRag Enterprise Knowledge Workbench With Extended Local Document Intelligence"
    $longNameMessage = Get-OnlyRagInstallerPrerequisiteStatus -WindowsSupported $true -WebView2Present $false -AppName $longProductName
    Assert-Condition -Condition ($longNameMessage.Message -like "*$longProductName*") -Message "Expected installer message to preserve long product name."
    Assert-InstallerMessageLayout -Message $longNameMessage.Message -Scenario "long product name"

    $missingSignToolPath = Join-Path ([System.IO.Path]::GetTempPath()) "onlyrag-missing-signtool-$([Guid]::NewGuid().ToString('N')).exe"
    $signToolMessage = $null
    try {
        Get-OnlyRagSignTool -RequestedPath $missingSignToolPath | Out-Null
    }
    catch {
        $signToolMessage = $_.Exception.Message
    }

    Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace($signToolMessage)) -Message "Expected missing signtool path to fail."
    foreach ($expected in @("Windows SDK signtool.exe", "Minimum supported version", "Why it is required", "Instruction", "Verify")) {
        Assert-Condition -Condition ($signToolMessage -like "*$expected*") -Message "Expected signtool prerequisite message to contain '$expected'."
    }

    Write-Host "Installer prerequisite self-test passed." -ForegroundColor Green
    exit 0
}

$windowsSupported = Test-OnlyRagSupportedWindows -Simulation $SimulateWindows
$webView2Present = Test-OnlyRagWebView2Runtime -Simulation $SimulateWebView2
$status = Get-OnlyRagInstallerPrerequisiteStatus -WindowsSupported $windowsSupported -WebView2Present $webView2Present

if ($status.CanInstall) {
    Write-Host $status.Message -ForegroundColor Green
    Write-Host (Get-OnlyRagNvidiaGpuOcrMemo -NvidiaToolsPresent (Test-OnlyRagNvidiaManagementTools)) -ForegroundColor Cyan
    exit 0
}

Write-Host $status.Message -ForegroundColor Red
exit 1
