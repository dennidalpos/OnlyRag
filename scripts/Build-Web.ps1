#requires -Version 7.0
[CmdletBinding()]
param(
    [switch]$SkipInstallWhenUpToDate
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$supportScript = Join-Path $PSScriptRoot "support\BuildSupport.ps1"
. $supportScript

$webRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\src\OnlyRag.Web")).Path

Invoke-OnlyRagWebBuild -WebRoot $webRoot -SkipInstallWhenUpToDate:$SkipInstallWhenUpToDate
