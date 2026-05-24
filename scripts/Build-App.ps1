#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$NoRestore,

    [switch]$SkipWebBuild
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$supportScript = Join-Path $PSScriptRoot "support\BuildSupport.ps1"
. $supportScript

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$solution = Join-Path $repoRoot "OnlyRag.sln"
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
    & $dotnetCommand.Source restore $solution
}

$dotnetCommand = Assert-OnlyRagDotNetSdk
& $dotnetCommand.Source build $solution --configuration $Configuration --no-restore
