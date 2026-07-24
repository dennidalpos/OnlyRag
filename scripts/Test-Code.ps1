#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$IncludeE2e
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$solution = Join-Path $repoRoot "OnlyRag.sln"
$webRoot = Join-Path $repoRoot "src\OnlyRag.Web"

Write-Host "Executing test suites across OnlyRag repository..." -ForegroundColor Cyan

# 1. Web Frontend Vitest Component Tests
Write-Host "==> Running Web Frontend Component Tests (Vitest)..." -ForegroundColor Yellow
Push-Location $webRoot
try {
    $global:LASTEXITCODE = 0
    if ($IncludeE2e) {
        npm test -- --run
    }
    else {
        npx vitest run
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Web frontend tests failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

# 2. .NET Solution Unit & Integration Tests
Write-Host "==> Running .NET Solution Tests (xUnit)..." -ForegroundColor Yellow
$global:LASTEXITCODE = 0
dotnet test $solution --configuration $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    throw ".NET tests failed with exit code $LASTEXITCODE."
}

Write-Host "All test suites completed successfully." -ForegroundColor Green
