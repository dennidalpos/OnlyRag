#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("win-x64")]
    [string]$RuntimeIdentifier = "win-x64",

    [switch]$NoRestore,

    [switch]$SkipWebBuild
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$supportScript = Join-Path $PSScriptRoot "support\BuildSupport.ps1"
. $supportScript

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$appProject = Join-Path $repoRoot "src\OnlyRag.App\OnlyRag.App.csproj"
$buildWebScript = Join-Path $PSScriptRoot "Build-Web.ps1"
$webIndex = Join-Path $repoRoot "src\OnlyRag.Web\dist\index.html"

if (-not $SkipWebBuild) {
    & $buildWebScript -SkipInstallWhenUpToDate
}

if (-not (Test-Path -LiteralPath $webIndex -PathType Leaf)) {
    throw "Web assets not found at $webIndex. Run scripts\Build-Web.ps1 first or rerun Build-App.ps1 without -SkipWebBuild."
}

if (-not $NoRestore) {
    $dotnetCommand = Assert-OnlyRagDotNetSdk
    Invoke-OnlyRagNative -FilePath $dotnetCommand.Source -WorkingDirectory $repoRoot -Arguments @(
        "restore",
        $appProject,
        "--runtime",
        $RuntimeIdentifier
    )
}

$dotnetCommand = Assert-OnlyRagDotNetSdk
Invoke-OnlyRagNative -FilePath $dotnetCommand.Source -WorkingDirectory $repoRoot -Arguments @(
    "build",
    $appProject,
    "--configuration",
    $Configuration,
    "--runtime",
    $RuntimeIdentifier,
    "--no-restore"
)
