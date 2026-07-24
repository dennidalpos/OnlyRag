#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$solution = Join-Path $repoRoot "OnlyRag.sln"
$webRoot = Join-Path $repoRoot "src\OnlyRag.Web"

Write-Host "Running static code analysis and linting across OnlyRag..." -ForegroundColor Cyan

# 1. Frontend Typecheck and ESLint
Write-Host "==> Linting Web Frontend (TypeScript + ESLint)..." -ForegroundColor Yellow
Push-Location $webRoot
try {
    $global:LASTEXITCODE = 0
    npm run typecheck
    if ($LASTEXITCODE -ne 0) {
        throw "TypeScript typecheck failed with exit code $LASTEXITCODE."
    }

    $global:LASTEXITCODE = 0
    npm run lint
    if ($LASTEXITCODE -ne 0) {
        throw "ESLint check failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

# 2. .NET Build & Analyzer Checks
Write-Host "==> Linting .NET Solution (Build & Analyzers)..." -ForegroundColor Yellow
$global:LASTEXITCODE = 0
dotnet build $solution --configuration $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    throw ".NET build & analyzer check failed with exit code $LASTEXITCODE."
}

Write-Host "Code linting and typechecking completed successfully." -ForegroundColor Green
