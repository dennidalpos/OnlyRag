#requires -Version 7.0
[CmdletBinding()]
param(
    [switch]$SkipNode,
    [switch]$SkipOcr,
    [switch]$SkipOllamaCheck,
    [switch]$NonInteractive,
    [string]$LibreOfficePath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$solutionPath = Join-Path $repoRoot "OnlyRag.sln"
$webRoot = Join-Path $repoRoot "src\OnlyRag.Web"
$ocrRoot = Join-Path $PSScriptRoot "ocr"
$ocrBridgePath = Join-Path $ocrRoot "paddle_ocr_bridge.py"
$ocrRequirementsPath = Join-Path $ocrRoot "requirements.txt"
$ocrManifestPath = Join-Path $ocrRoot "runtime-manifest.json"
$localDataRoot = Join-Path $env:LOCALAPPDATA "OnlyRag"
$supportedNodeVersionText = "Node.js 20.19.x or 22.12+"

$script:Failures = [System.Collections.Generic.List[string]]::new()
$script:Warnings = [System.Collections.Generic.List[string]]::new()
$script:Verified = [System.Collections.Generic.List[string]]::new()
$script:Installed = [System.Collections.Generic.List[string]]::new()
$script:Manual = [System.Collections.Generic.List[string]]::new()

. (Join-Path $PSScriptRoot "support\BootstrapPrerequisites.Core.ps1")
. (Join-Path $PSScriptRoot "support\BootstrapPrerequisites.OcrRuntime.ps1")
. (Join-Path $PSScriptRoot "support\BootstrapPrerequisites.Ocr.ps1")

Write-Host "OnlyRag Windows bootstrap" -ForegroundColor Cyan
Write-Host "Repository: $repoRoot"
Write-Host "Mode: bootstrap only; build, packaging, deploy, and release are not executed."
if ($NonInteractive) {
    Write-Host "NonInteractive: no prompts and no system-level installers." -ForegroundColor Cyan
}

if (-not $IsWindows) {
    Add-Failure "OnlyRag targets Windows; this bootstrap must be run on Windows."
}
else {
    Add-Verified "Windows host detected."
}

if ($PSVersionTable.PSEdition -ne "Core" -or $PSVersionTable.PSVersion.Major -lt 7) {
    Add-Failure "PowerShell 7+ is required. Current version: $($PSVersionTable.PSVersion)."
}
else {
    Add-Verified "PowerShell $($PSVersionTable.PSVersion) detected."
}

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$canRunDotnet = $false
if (-not $dotnetCommand) {
    Add-Failure ".NET CLI was not found. Install .NET 10 SDK for Windows."
}
else {
    $sdks = @(& $dotnetCommand.Source --list-sdks)
    $runtimes = @(& $dotnetCommand.Source --list-runtimes)
    $sdk10 = @($sdks | Where-Object { $_ -match '^10\.' })
    $netRuntime10 = @($runtimes | Where-Object { $_ -match '^Microsoft\.NETCore\.App\s+10\.' })
    $desktopRuntime10 = @($runtimes | Where-Object { $_ -match '^Microsoft\.WindowsDesktop\.App\s+10\.' })
    $aspNetRuntime10 = @($runtimes | Where-Object { $_ -match '^Microsoft\.AspNetCore\.App\s+10\.' })

    if ($sdk10.Count -eq 0) {
        Add-Failure ".NET 10 SDK was not found. Install .NET 10 SDK for Windows."
    }
    else {
        Add-Verified ".NET 10 SDK detected: $($sdk10[0])."
    }

    if ($netRuntime10.Count -eq 0) {
        Add-Failure ".NET 10 runtime was not found."
    }
    else {
        Add-Verified ".NET 10 runtime detected: $($netRuntime10[0])."
    }

    if ($desktopRuntime10.Count -eq 0) {
        Add-Failure ".NET 10 Windows Desktop runtime was not found."
    }
    else {
        Add-Verified ".NET 10 Windows Desktop runtime detected: $($desktopRuntime10[0])."
    }

    if ($aspNetRuntime10.Count -eq 0) {
        Add-Failure ".NET 10 ASP.NET Core runtime was not found."
    }
    else {
        Add-Verified ".NET 10 ASP.NET Core runtime detected: $($aspNetRuntime10[0])."
    }

    $canRunDotnet = $sdk10.Count -gt 0
}

$webView2Runtime = Get-WebView2Runtime
if ($webView2Runtime) {
    Add-Verified "WebView2 Runtime detected: $($webView2Runtime.Version)."
}
else {
    Add-Failure "WebView2 Runtime was not found. Install Microsoft Edge WebView2 Runtime manually."
}

$canRunNpm = $false
if ($SkipNode) {
    Write-Result -Status "SKIP" -Message "Node.js and npm checks skipped by -SkipNode."
    if (Test-Path -LiteralPath (Join-Path $webRoot "package-lock.json") -PathType Leaf) {
        Add-Manual "Run npm ci in src\OnlyRag.Web before building the UI."
    }
    else {
        Add-Manual "Run npm install in src\OnlyRag.Web before building the UI."
    }
}
else {
    $nodeCommand = Get-Command node -ErrorAction SilentlyContinue
    $npmCommand = Get-Command npm -ErrorAction SilentlyContinue

    if (-not $nodeCommand) {
        Add-Failure "Node.js was not found. Install $supportedNodeVersionText for Windows."
    }
    else {
        $nodeVersionText = (& $nodeCommand.Source --version 2>&1 | Out-String).Trim()
        $nodeVersion = ConvertTo-VersionOrNull $nodeVersionText
        if (-not $nodeVersion -or -not (Test-NodeSupportedVersion -Version $nodeVersion)) {
            Add-Failure "$supportedNodeVersionText is required; found '$nodeVersionText'."
        }
        else {
            Add-Verified "Node.js $nodeVersionText detected."
        }
    }

    if (-not $npmCommand) {
        Add-Failure "npm was not found. Install Node.js with npm."
    }
    else {
        $npmVersionText = (& $npmCommand.Source --version 2>&1 | Out-String).Trim()
        Add-Verified "npm detected: $npmVersionText."
    }

    $canRunNpm = [bool]$nodeCommand -and [bool]$npmCommand
}

if ($SkipOllamaCheck) {
    Write-Result -Status "SKIP" -Message "Ollama check skipped by -SkipOllamaCheck."
}
else {
    $ollamaCommand = Get-Command ollama -ErrorAction SilentlyContinue
    if (-not $ollamaCommand) {
        Add-Warning "Ollama CLI was not found. OnlyRag can run, but local chat/RAG models require Ollama."
        Add-Manual "Install Ollama for Windows from https://ollama.com/download and start it before using model features."
    }
    else {
        Add-Verified "Ollama CLI detected: $($ollamaCommand.Source)."
        try {
            $ollamaResponse = Invoke-WebRequest -Uri "http://localhost:11434/api/tags" -UseBasicParsing -TimeoutSec 5
            if ($ollamaResponse.StatusCode -ge 200 -and $ollamaResponse.StatusCode -lt 300) {
                Add-Verified "Ollama endpoint reachable at http://localhost:11434."
            }
            else {
                Add-Warning "Ollama endpoint returned HTTP $($ollamaResponse.StatusCode) at http://localhost:11434."
            }
        }
        catch {
            Add-Warning "Ollama CLI is installed, but http://localhost:11434 is not reachable. Start Ollama before using model features."
        }
    }
}

$libreOfficeExe = Get-LibreOfficeExecutable -RequestedPath $LibreOfficePath
if ($libreOfficeExe) {
    Add-Verified "LibreOffice converter detected: $libreOfficeExe."
}
else {
    Add-Warning "LibreOffice was not found. Legacy Office ingestion remains optional and manual."
    Add-Manual "Install LibreOffice for Windows or set ONLYRAG_LIBREOFFICE_PATH for .doc, .xls, and .ppt ingestion."
}

try {
    Ensure-OnlyRagDirectories
}
catch {
    Add-Failure "Could not create %LOCALAPPDATA%\OnlyRag directories: $($_.Exception.Message)"
}

Ensure-OcrEnvironment

if ($canRunDotnet) {
    try {
        Invoke-Native -FilePath $dotnetCommand.Source -Arguments @("restore", $solutionPath)
        Add-Installed "dotnet restore completed for OnlyRag.sln."
    }
    catch {
        Add-Failure "dotnet restore failed: $($_.Exception.Message)"
    }
}
else {
    Add-Manual "dotnet restore was not run because .NET 10 SDK is missing."
}

if (-not $SkipNode -and $canRunNpm) {
    try {
        if (Test-Path -LiteralPath (Join-Path $webRoot "package-lock.json") -PathType Leaf) {
            Invoke-Native -FilePath $npmCommand.Source -Arguments @("ci") -WorkingDirectory $webRoot
            Add-Installed "npm ci completed in src\OnlyRag.Web."
        }
        else {
            Invoke-Native -FilePath $npmCommand.Source -Arguments @("install") -WorkingDirectory $webRoot
            Add-Installed "npm install completed in src\OnlyRag.Web."
        }
    }
    catch {
        Add-Failure "npm dependency installation failed in src\OnlyRag.Web: $($_.Exception.Message)"
    }
}

Write-Host ""
Write-Host "Summary" -ForegroundColor Cyan
Write-Host "Installed or prepared:"
foreach ($item in $script:Installed) {
    Write-Host "  - $item"
}
if ($script:Installed.Count -eq 0) {
    Write-Host "  - none"
}

Write-Host "Verified:"
foreach ($item in $script:Verified) {
    Write-Host "  - $item"
}
if ($script:Verified.Count -eq 0) {
    Write-Host "  - none"
}

Write-Host "Manual or optional:"
foreach ($item in $script:Manual) {
    Write-Host "  - $item"
}
if ($script:Manual.Count -eq 0) {
    Write-Host "  - none"
}

if ($script:Warnings.Count -gt 0) {
    Write-Host "Warnings:" -ForegroundColor Yellow
    foreach ($item in $script:Warnings) {
        Write-Host "  - $item"
    }
}

if ($script:Failures.Count -gt 0) {
    Write-Host "Blocking failures:" -ForegroundColor Red
    foreach ($item in $script:Failures) {
        Write-Host "  - $item"
    }
    exit 1
}

Write-Host "Bootstrap completed. This did not build, package, deploy, sign, or release OnlyRag." -ForegroundColor Green
exit 0
