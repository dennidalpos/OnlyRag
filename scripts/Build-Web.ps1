#requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$supportScript = Join-Path $PSScriptRoot "internal\BuildSupport.ps1"
. $supportScript

$webRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\src\OnlyRag.Web")).Path

Invoke-OnlyRagWebBuild -WebRoot $webRoot
