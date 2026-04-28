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

function Write-Result {
    param(
        [ValidateSet("OK", "WARN", "FAIL", "INFO", "SKIP")]
        [string]$Status,
        [string]$Message
    )

    $color = switch ($Status) {
        "OK" { "Green" }
        "WARN" { "Yellow" }
        "FAIL" { "Red" }
        "SKIP" { "DarkYellow" }
        default { "Cyan" }
    }

    Write-Host ("[{0}] {1}" -f $Status, $Message) -ForegroundColor $color
}

function Add-Verified {
    param([string]$Message)
    $script:Verified.Add($Message)
    Write-Result -Status "OK" -Message $Message
}

function Add-Installed {
    param([string]$Message)
    $script:Installed.Add($Message)
    Write-Result -Status "OK" -Message $Message
}

function Add-Warning {
    param([string]$Message)
    $script:Warnings.Add($Message)
    Write-Result -Status "WARN" -Message $Message
}

function Add-Failure {
    param([string]$Message)
    $script:Failures.Add($Message)
    Write-Result -Status "FAIL" -Message $Message
}

function Add-Manual {
    param([string]$Message)
    $script:Manual.Add($Message)
    Write-Result -Status "INFO" -Message $Message
}

function Invoke-Native {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,
        [Parameter()]
        [string[]]$Arguments = @(),
        [Parameter()]
        [string]$WorkingDirectory = $repoRoot
    )

    Push-Location $WorkingDirectory
    try {
        & $FilePath @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "'$FilePath $($Arguments -join ' ')' failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

function ConvertTo-VersionOrNull {
    param([string]$Text)

    if ($Text -match 'v?(\d+)\.(\d+)\.(\d+)') {
        return [version]::new([int]$Matches[1], [int]$Matches[2], [int]$Matches[3])
    }

    return $null
}

function Test-NodeSupportedVersion {
    param([version]$Version)

    return (
        ($Version.Major -eq 20 -and $Version.Minor -ge 19) -or
        ($Version.Major -eq 22 -and $Version.Minor -ge 12) -or
        ($Version.Major -gt 22)
    )
}

function Get-WebView2Runtime {
    $edgeUpdateRoots = @(
        "HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients",
        "HKCU:\SOFTWARE\Microsoft\EdgeUpdate\Clients"
    )

    foreach ($root in $edgeUpdateRoots) {
        if (-not (Test-Path -LiteralPath $root)) {
            continue
        }

        foreach ($clientKey in Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue) {
            $props = Get-ItemProperty -LiteralPath $clientKey.PSPath -ErrorAction SilentlyContinue
            $name = $props.name
            if ($name -and $name -like "*WebView2*Runtime*") {
                return [pscustomobject]@{
                    Version = $props.pv
                    Source = $clientKey.PSPath
                }
            }
        }
    }

    $applicationRoots = @()
    foreach ($basePath in @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:LOCALAPPDATA)) {
        if ([string]::IsNullOrWhiteSpace($basePath)) {
            continue
        }

        $applicationRoots += Join-Path $basePath "Microsoft\EdgeWebView\Application"
    }

    foreach ($applicationRoot in $applicationRoots) {
        if (-not (Test-Path -LiteralPath $applicationRoot -PathType Container)) {
            continue
        }

        $runtimeExe = Get-ChildItem -LiteralPath $applicationRoot -Recurse -Filter "msedgewebview2.exe" -File -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($runtimeExe) {
            return [pscustomobject]@{
                Version = $runtimeExe.Directory.Name
                Source = $runtimeExe.FullName
            }
        }
    }

    return $null
}

function Get-LibreOfficeExecutable {
    param([string]$RequestedPath)

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $candidates += $RequestedPath
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ONLYRAG_LIBREOFFICE_PATH)) {
        $candidates += $env:ONLYRAG_LIBREOFFICE_PATH
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $candidates += Join-Path $env:ProgramFiles "LibreOffice\program\soffice.exe"
    }
    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
        $candidates += Join-Path ${env:ProgramFiles(x86)} "LibreOffice\program\soffice.exe"
    }

    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }

        if (Test-Path -LiteralPath $candidate -PathType Container) {
            $nestedCandidate = Join-Path $candidate "program\soffice.exe"
            if (Test-Path -LiteralPath $nestedCandidate -PathType Leaf) {
                return (Resolve-Path -LiteralPath $nestedCandidate).Path
            }
        }
    }

    return $null
}

function Ensure-OnlyRagDirectories {
    $directories = @(
        $localDataRoot,
        (Join-Path $localDataRoot "data"),
        (Join-Path $localDataRoot "documents"),
        (Join-Path $localDataRoot "documents\originals"),
        (Join-Path $localDataRoot "documents\renders"),
        (Join-Path $localDataRoot "documents\ocr-cache"),
        (Join-Path $localDataRoot "documents\exports"),
        (Join-Path $localDataRoot "logs"),
        (Join-Path $localDataRoot "temp")
    )

    foreach ($directory in $directories) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    Add-Installed "Local data directories ready under $localDataRoot."
}

function Get-OcrRuntimeManifest {
    if (-not (Test-Path -LiteralPath $ocrManifestPath -PathType Leaf)) {
        Add-Warning "OCR runtime manifest was not found. Falling back to requirements.txt only."
        return $null
    }

    return Get-Content -Raw -LiteralPath $ocrManifestPath | ConvertFrom-Json
}

function Test-OcrDiskSpace {
    param([object]$Manifest)

    if (-not $Manifest -or -not $Manifest.minimumFreeDiskBytes) {
        return
    }

    $driveRoot = [System.IO.Path]::GetPathRoot($localDataRoot)
    $drive = Get-PSDrive -Name $driveRoot.TrimEnd(":\") -ErrorAction SilentlyContinue
    if (-not $drive) {
        Add-Warning "Could not determine free disk space for OCR runtime at $localDataRoot."
        return
    }

    [int64]$requiredBytes = $Manifest.minimumFreeDiskBytes
    if ($drive.Free -lt $requiredBytes) {
        Add-Warning "OCR runtime may need at least $([math]::Round($requiredBytes / 1GB, 1)) GB free; available on $driveRoot is $([math]::Round($drive.Free / 1GB, 1)) GB."
    }
    else {
        Add-Verified "OCR runtime disk space check passed: $([math]::Round($drive.Free / 1GB, 1)) GB available."
    }
}

function Test-OcrPinnedPackageSet {
    param(
        [Parameter(Mandatory)]
        [string]$PythonPath,
        [object]$Manifest
    )

    if (-not $Manifest -or -not $Manifest.packages) {
        return
    }

    $verifyScript = Join-Path (Split-Path -Parent $PythonPath) "onlyrag_verify_ocr_runtime.py"
    $packagesJson = ($Manifest.packages | ConvertTo-Json -Compress)
    $python = @"
import importlib
import importlib.metadata
import json
import sys

packages = json.loads(r'''$packagesJson''')
failures = []
for package in packages:
    name = package["name"]
    expected = package["version"]
    import_name = package["importName"]
    try:
        actual = importlib.metadata.version(name)
    except importlib.metadata.PackageNotFoundError:
        failures.append(f"{name} missing")
        continue
    if actual != expected:
        failures.append(f"{name} expected {expected}, found {actual}")
    try:
        importlib.import_module(import_name)
    except Exception as exc:
        failures.append(f"{import_name} import failed: {exc}")

if failures:
    print("; ".join(failures), file=sys.stderr)
    raise SystemExit(1)

print("OCR runtime packages verified.")
"@
    [System.IO.File]::WriteAllText($verifyScript, $python)
    try {
        Invoke-Native -FilePath $PythonPath -Arguments @($verifyScript)
        Add-Verified "OCR package versions and imports match scripts\ocr\runtime-manifest.json."
        Invoke-Native -FilePath $PythonPath -Arguments @("-m", "pip", "check")
        Add-Verified "OCR Python dependency graph passed pip check."
        Invoke-Native -FilePath $PythonPath -Arguments @($ocrBridgePath, "--mode", "check")
        Add-Verified "OCR bridge check completed with pinned runtime."
    }
    finally {
        Remove-Item -LiteralPath $verifyScript -Force -ErrorAction SilentlyContinue
    }
}

function Ensure-OcrEnvironment {
    if ($SkipOcr) {
        Write-Result -Status "SKIP" -Message "OCR preparation skipped by -SkipOcr."
        Add-Manual "OCR remains manual. Default path: %LOCALAPPDATA%\OnlyRag\ocr-python\.venv\Scripts\python.exe."
        return
    }

    if (-not (Test-Path -LiteralPath $ocrBridgePath -PathType Leaf) -or -not (Test-Path -LiteralPath $ocrRequirementsPath -PathType Leaf)) {
        Add-Warning "OCR bridge or requirements were not found under scripts\ocr; OCR preparation skipped."
        return
    }

    $ocrManifest = Get-OcrRuntimeManifest
    Test-OcrDiskSpace -Manifest $ocrManifest

    $pythonCommand = Get-Command python -ErrorAction SilentlyContinue
    if (-not $pythonCommand) {
        Add-Warning "Python was not found. OCR is optional; install Python 3.10+ for Windows to enable PaddleOCR."
        Add-Manual "After installing Python, rerun pwsh .\scripts\Bootstrap-Prerequisites.ps1 or set ONLYRAG_OCR_PYTHON manually."
        return
    }

    $pythonVersionText = (& $pythonCommand.Source --version 2>&1 | Out-String).Trim()
    $pythonVersion = ConvertTo-VersionOrNull $pythonVersionText
    if (-not $pythonVersion -or $pythonVersion.Major -lt 3 -or ($pythonVersion.Major -eq 3 -and $pythonVersion.Minor -lt 10)) {
        Add-Warning "OCR requires Python 3.10+; found '$pythonVersionText'. OCR preparation skipped."
        Add-Manual "Install Python 3.10+ for Windows, then rerun bootstrap without -SkipOcr."
        return
    }

    Add-Verified "Python available for OCR: $pythonVersionText."

    $ocrInstallRoot = Join-Path $localDataRoot "ocr-python"
    $venvPath = Join-Path $ocrInstallRoot ".venv"
    $venvPython = Join-Path $venvPath "Scripts\python.exe"

    try {
        New-Item -ItemType Directory -Force -Path $ocrInstallRoot | Out-Null

        $venvNeedsCreate = $false
        if (-not (Test-Path -LiteralPath $venvPython -PathType Leaf)) {
            $venvNeedsCreate = $true
        }
        else {
            $venvVersionText = (& $venvPython --version 2>&1 | Out-String).Trim()
            $venvVersion = ConvertTo-VersionOrNull $venvVersionText
            if (-not $venvVersion) {
                Add-Warning "OCR venv Python non funzionante (creato con una versione diversa di Python). Ricreo il venv..."
                Remove-Item -Recurse -Force $venvPath
                $venvNeedsCreate = $true
            }
        }

        if ($venvNeedsCreate) {
            Invoke-Native -FilePath $pythonCommand.Source -Arguments @("-m", "venv", $venvPath)
            Add-Installed "OCR Python virtual environment created at $venvPath."
        }
        else {
            Add-Verified "OCR Python virtual environment already present at $venvPath."
        }

        Invoke-Native -FilePath $venvPython -Arguments @("-m", "pip", "install", "--upgrade", "pip", "--disable-pip-version-check")

        # Upgrade OCR packages only when requirements.txt has changed; otherwise just install missing.
        $requirementsStamp = Join-Path $venvPath ".requirements-stamp"
        $requirementsChanged = $true
        if (Test-Path -LiteralPath $requirementsStamp) {
            $stampMtime = (Get-Item $requirementsStamp).LastWriteTimeUtc
            $reqMtime   = (Get-Item $ocrRequirementsPath).LastWriteTimeUtc
            $requirementsChanged = $reqMtime -gt $stampMtime
        }

        if ($requirementsChanged) {
            Write-Host "  requirements.txt changed — upgrading OCR packages..." -ForegroundColor Cyan
            Invoke-Native -FilePath $venvPython -Arguments @("-m", "pip", "install", "--upgrade", "-r", $ocrRequirementsPath, "--disable-pip-version-check")
        }
        else {
            Write-Host "  requirements.txt unchanged — installing missing OCR packages only..." -ForegroundColor DarkGray
            Invoke-Native -FilePath $venvPython -Arguments @("-m", "pip", "install", "-r", $ocrRequirementsPath, "--disable-pip-version-check")
        }
        (Get-Item $ocrRequirementsPath).LastWriteTimeUtc | Out-Null
        [System.IO.File]::WriteAllText($requirementsStamp, (Get-Date -Format 'o'))
        Add-Installed "OCR Python packages prepared from scripts\ocr\requirements.txt."
        Test-OcrPinnedPackageSet -PythonPath $venvPython -Manifest $ocrManifest
        Add-Manual "Set ONLYRAG_OCR_PYTHON=$venvPython only when running outside the default user profile."
        Add-Manual "PaddleOCR downloads models on first OCR use into the user profile cache; keep at least 5 GB free for packages and models."
    }
    catch {
        Add-Warning "OCR preparation failed: $($_.Exception.Message)"
        Add-Manual "OCR is optional. Rerun without -SkipOcr after fixing Python/pip/network access, or set ONLYRAG_OCR_PYTHON to a prepared environment."
    }
}

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
