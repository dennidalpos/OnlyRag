#requires -Version 7.0
[CmdletBinding()]
param(
    [switch]$CheckOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$solution = Join-Path $repoRoot "OnlyRag.sln"
$webRoot = Join-Path $repoRoot "src\OnlyRag.Web"

Write-Host "Formatting code across OnlyRag repository..." -ForegroundColor Cyan

# 1. Format .NET Solution
Write-Host "==> Formatting .NET C# solution..." -ForegroundColor Yellow
$global:LASTEXITCODE = 0
if ($CheckOnly) {
    dotnet format $solution --verify-no-changes
}
else {
    dotnet format $solution
}

if ($LASTEXITCODE -ne 0) {
    throw ".NET formatting check/execution failed with exit code $LASTEXITCODE."
}

# 2. Format Web Frontend
Write-Host "==> Formatting Web Frontend (Prettier)..." -ForegroundColor Yellow
Push-Location $webRoot
try {
    $global:LASTEXITCODE = 0
    if ($CheckOnly) {
        npm run format:check
    }
    else {
        npm run format
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Web frontend formatting check/execution failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

Write-Host "Code formatting completed successfully." -ForegroundColor Green
