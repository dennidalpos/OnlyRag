#requires -Version 7.0
<#
.SYNOPSIS
    Full clean-build gate: kills open processes, cleans artifacts, builds web+app, runs tests, packages installer.
.PARAMETER Configuration
    Debug or Release (default: Release).
.PARAMETER Version
    SemVer string embedded in the build and installer (default: 0.1.0).
.PARAMETER SkipTests
    Skip the test suite (useful for packaging-only reruns).
.PARAMETER SkipInstaller
    Skip Inno Setup compilation (skipped automatically when ISCC.exe is absent).
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$Version = "0.1.0",

    [switch]$SkipTests,

    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$supportScript = Join-Path $PSScriptRoot "..\internal\BuildSupport.ps1"
. $supportScript

$repoRoot     = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$solution     = Join-Path $repoRoot "OnlyRag.sln"
$webRoot      = Join-Path $repoRoot "src\OnlyRag.Web"
$appProject   = Join-Path $repoRoot "src\OnlyRag.App\OnlyRag.App.csproj"
$appIcon      = Join-Path $repoRoot "src\OnlyRag.App\Assets\OnlyRag.ico"
$innoScript   = Join-Path $repoRoot "packaging\OnlyRag.iss"
$artifactsDir = Join-Path $repoRoot "artifacts"
$publishDir   = Join-Path $artifactsDir "publish\OnlyRag\win-x64"
$installerDir = Join-Path $artifactsDir "installer"

$runtimeIdentifier = "win-x64"

$script:stepIndex = 0

function Write-Step {
    param([string]$Message)
    $script:stepIndex++
    Write-Host ""
    Write-Host "[$($script:stepIndex)] $Message" -ForegroundColor Cyan
}

function Stop-ProcessesLockingPath {
    # Kill any process whose executable lives under $RootPath, plus WebView2 referencing it.
    param([string]$RootPath)
    $norm = $RootPath.TrimEnd('\') + '\'
    Get-Process -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and $_.Path.StartsWith($norm, [System.StringComparison]::OrdinalIgnoreCase) }
        catch { $false }
    } | ForEach-Object {
        Write-Host "  Killing $($_.Name)  PID $($_.Id)  (locks $RootPath)" -ForegroundColor Yellow
        $_ | Stop-Process -Force -ErrorAction SilentlyContinue
    }
    Get-CimInstance Win32_Process -Filter "Name = 'msedgewebview2.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine -like "*$RootPath*" } |
        ForEach-Object {
            Write-Host "  Killing msedgewebview2  PID $($_.ProcessId)  (locks $RootPath)" -ForegroundColor Yellow
            Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
        }
}

# ── 1. Kill processes that lock build outputs ─────────────────────────────────
Write-Step "Terminating processes that may lock build outputs"

function Stop-NamedProcesses {
    param([string[]]$Names)
    foreach ($name in $Names) {
        $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
        foreach ($p in $procs) {
            Write-Host "  Stopping $($p.Name)  PID $($p.Id)" -ForegroundColor Yellow
            $p | Stop-Process -Force -ErrorAction SilentlyContinue
        }
    }
}

# Capture OnlyRag.App PIDs before killing so we can chase their WebView2 children.
$onlyRagPids = @(Get-Process -Name "OnlyRag.App" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)

# Direct targets: app itself, .NET test hosts, Inno Setup compiler.
Stop-NamedProcesses -Names @("OnlyRag.App", "OnlyRag", "testhost", "ISCC")

# WebView2 child processes spawned by the app.
if ($onlyRagPids) {
    $webviewProcs = Get-CimInstance Win32_Process -Filter "Name = 'msedgewebview2.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.ParentProcessId -in $onlyRagPids }
    foreach ($wp in $webviewProcs) {
        Write-Host "  Stopping msedgewebview2  PID $($wp.ProcessId)  (child of OnlyRag.App)" -ForegroundColor Yellow
        Stop-Process -Id $wp.ProcessId -Force -ErrorAction SilentlyContinue
    }
}

# Catch any surviving msedgewebview2 whose command line references this repo
# (e.g. orphaned processes whose OnlyRag.App parent already exited).
Get-CimInstance Win32_Process -Filter "Name = 'msedgewebview2.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -and $_.CommandLine -like "*$repoRoot*" } |
    ForEach-Object {
        Write-Host "  Stopping msedgewebview2  PID $($_.ProcessId)  (references repo)" -ForegroundColor Yellow
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }

# Node.js processes running Vite from inside this repo (dev server).
foreach ($np in (Get-Process -Name "node" -ErrorAction SilentlyContinue)) {
    $cmdLine = (Get-CimInstance Win32_Process -Filter "ProcessId = $($np.Id)" -ErrorAction SilentlyContinue).CommandLine
    if ($cmdLine -and $cmdLine -like "*$repoRoot*") {
        Write-Host "  Stopping node (Vite)  PID $($np.Id)" -ForegroundColor Yellow
        $np | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}

# Give the OS a moment to release file handles.
Start-Sleep -Milliseconds 1500

# Pre-check NuGet state before cleaning obj directories.
$nugetUpToDate = Test-OnlyRagNuGetRestoreUpToDate -SolutionRoot $repoRoot

# ── 2. Wipe local user data (fresh-only build) ───────────────────────────────
Write-Step "Wiping local user data (fresh-only)"

$userDataRoot = Join-Path $env:LOCALAPPDATA "OnlyRag"

# Wipe only app-data subdirs; preserve installed tooling (ocr-python, etc.).
$userDataDirs = @("data", "documents", "logs", "temp")

if (Test-Path -LiteralPath $userDataRoot) {
    $localAppDataNorm = [System.IO.Path]::GetFullPath($env:LOCALAPPDATA).TrimEnd('\') + '\'
    $userDataNorm     = [System.IO.Path]::GetFullPath($userDataRoot)
    if (-not $userDataNorm.StartsWith($localAppDataNorm, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "User-data path is outside %LOCALAPPDATA% — aborting wipe."
    }

    foreach ($sub in $userDataDirs) {
        $subPath = Join-Path $userDataRoot $sub
        if (Test-Path -LiteralPath $subPath) {
            Write-Host "  Removing $subPath" -ForegroundColor Yellow
            Remove-OnlyRagDirectoryRobust -Path $subPath -BeforeRetry {
                Write-Host "  Remove failed - killing locking processes..." -ForegroundColor Yellow
                Stop-ProcessesLockingPath -RootPath $subPath
            }
        }
    }
    Write-Host "  User data wiped (tooling directories preserved)." -ForegroundColor Green
}
else {
    Write-Host "  No user data found at $userDataRoot — nothing to wipe." -ForegroundColor DarkGray
}

# ── 3. Clean artifacts directory ─────────────────────────────────────────────
Write-Step "Cleaning artifacts directory"

$protectedRoot = $repoRoot.TrimEnd('\') + '\'
$fullArtifacts = [System.IO.Path]::GetFullPath($artifactsDir)
if (-not $fullArtifacts.StartsWith($protectedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Artifacts path is outside the repository — aborting clean."
}

foreach ($dir in @($publishDir, $installerDir)) {
    if (Test-Path -LiteralPath $dir) {
        Write-Host "  Removing $dir"
        Remove-OnlyRagDirectoryRobust -Path $dir -BeforeRetry {
            Write-Host "  Remove failed - killing locking processes..." -ForegroundColor Yellow
            Stop-ProcessesLockingPath -RootPath $dir
        }
    }
}

# ── 4. Clean .NET intermediate / output directories ───────────────────────────
Write-Step "Cleaning .NET bin/obj directories"

Get-ChildItem -Path $repoRoot -Recurse -Force -Directory |
    Where-Object { $_.Name -eq "bin" -or (-not $nugetUpToDate -and $_.Name -eq "obj") } |
    Where-Object { $_.FullName -notlike "*\node_modules\*" } |
    ForEach-Object {
        Write-Host "  Removing $($_.FullName)"
        Remove-Item -LiteralPath $_.FullName -Recurse -Force
    }

if ($nugetUpToDate) {
    Write-Host "  obj preserved — NuGet packages up to date" -ForegroundColor DarkGray
}

# ── 5. Clean React dist ───────────────────────────────────────────────────────
Write-Step "Cleaning React dist"

$webDist = Join-Path $webRoot "dist"
if (Test-Path -LiteralPath $webDist) {
    Write-Host "  Removing $webDist"
    Remove-Item -LiteralPath $webDist -Recurse -Force
}

# ── 6. Verify toolchain ───────────────────────────────────────────────────────
Write-Step "Verifying toolchain"

$dotnet = Get-Command "dotnet" -ErrorAction SilentlyContinue
if (-not $dotnet) { throw ".NET SDK not found. Install .NET 10 SDK for Windows." }
Write-Host "  dotnet  : $($dotnet.Source)"

$npm = Get-Command "npm" -ErrorAction SilentlyContinue
if (-not $npm) { throw "npm not found. Install Node.js." }
Write-Host "  npm     : $($npm.Source)"

# ── 7. Build React/Vite UI ────────────────────────────────────────────────────
Write-Step "Building React / Vite UI"

Invoke-OnlyRagWebBuild -WebRoot $webRoot -SkipInstallWhenUpToDate

# ── 8. Restore & build .NET solution ─────────────────────────────────────────
Write-Step "Restoring NuGet packages"

if ($nugetUpToDate) {
    Write-Host "  NuGet packages up to date — skipping dotnet restore" -ForegroundColor DarkGray
}
else {
    Invoke-OnlyRagNative -FilePath $dotnet.Source -Arguments @("restore", $solution) -WorkingDirectory $repoRoot
}

Write-Step "Building solution ($Configuration)"
Invoke-OnlyRagNative -FilePath $dotnet.Source -WorkingDirectory $repoRoot -Arguments @(
    "build", $solution,
    "--configuration", $Configuration,
    "--no-restore"
)

# ── 9. Run test suite ─────────────────────────────────────────────────────────
if (-not $SkipTests) {
    Write-Step "Running tests"
    Invoke-OnlyRagNative -FilePath $dotnet.Source -WorkingDirectory $repoRoot -Arguments @(
        "test", $solution,
        "--configuration", $Configuration,
        "--no-build",
        "--logger", "console;verbosity=minimal"
    )
}
else {
    Write-Host ""
    Write-Host "[--] Tests skipped (SkipTests flag set)" -ForegroundColor DarkGray
}

# ── 10. Publish self-contained app ────────────────────────────────────────────
Write-Step "Publishing OnlyRag.App ($runtimeIdentifier)"

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

Invoke-OnlyRagNative -FilePath $dotnet.Source -WorkingDirectory $repoRoot -Arguments @(
    "publish", $appProject,
    "--configuration", $Configuration,
    "--runtime", $runtimeIdentifier,
    "--self-contained", "false",
    "--output", $publishDir,
    "/p:Version=$Version",
    "/p:PublishSingleFile=false"
)

# ── 11. Validate publish payload ──────────────────────────────────────────────
Write-Step "Validating publish payload"

Test-OnlyRagPublishPayload -Path $publishDir
Write-Host "  Payload OK — $((Get-ChildItem -LiteralPath $publishDir -Recurse -File).Count) files"

# ── 12. Compile Inno Setup installer ─────────────────────────────────────────
if (-not $SkipInstaller) {
    Write-Step "Compiling Inno Setup installer (v$Version)"

    $iscc = Get-OnlyRagInnoSetupCompiler

    if (-not $iscc) {
        Write-Host "  ISCC.exe not found — installer skipped. Install Inno Setup 6 to enable." -ForegroundColor Yellow
    }
    else {
        New-Item -ItemType Directory -Force -Path $installerDir | Out-Null

        Invoke-OnlyRagNative -FilePath $iscc -WorkingDirectory $repoRoot -Arguments @(
            "/DAppVersion=$Version",
            "/DRuntimeIdentifier=$runtimeIdentifier",
            "/DPublishDir=$publishDir",
            "/DOutputDir=$installerDir",
            "/DAppIcon=$appIcon",
            $innoScript
        )

        $installer = Get-ChildItem -LiteralPath $installerDir -Filter "*.exe" -File |
                     Sort-Object LastWriteTime -Descending |
                     Select-Object -First 1
        if (-not $installer) {
            throw "ISCC.exe finished but no installer .exe found in $installerDir"
        }

        Write-Host "  Installer: $($installer.FullName)" -ForegroundColor Green
    }
}
else {
    Write-Host ""
    Write-Host "[--] Installer skipped (SkipInstaller flag set)" -ForegroundColor DarkGray
}

# ── Summary ───────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Green
Write-Host "  Gate build complete  |  $Configuration  |  v$Version" -ForegroundColor Green
Write-Host "  Publish : $publishDir" -ForegroundColor Green
if (-not $SkipInstaller -and (Test-Path -LiteralPath $installerDir)) {
    $ins = Get-ChildItem -LiteralPath $installerDir -Filter "*.exe" -File |
           Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($ins) { Write-Host "  Installer: $($ins.FullName)" -ForegroundColor Green }
}
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Green
Write-Host "  NOTE: Output is not signed, installed, or released." -ForegroundColor Yellow
