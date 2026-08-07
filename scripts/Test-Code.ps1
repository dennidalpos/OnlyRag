#requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$IncludeE2e,

    [switch]$Fast,

    [switch]$IncludeIntegration,

    [string]$Filter,

    [switch]$VerboseOutput
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$env:ONLYRAG_TEST_ENVIRONMENT = "true"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$solution = Join-Path $repoRoot "OnlyRag.sln"
$coreTestsProject = Join-Path $repoRoot "tests\OnlyRag.Core.Tests\OnlyRag.Core.Tests.csproj"
$webRoot = Join-Path $repoRoot "src\OnlyRag.Web"
$gateDiagnosticsScript = Join-Path $PSScriptRoot "support\GateDiagnostics.ps1"
. $gateDiagnosticsScript

Write-Host "Executing test suites across OnlyRag repository..." -ForegroundColor Cyan
Write-Host "Mode: $(if ($VerboseOutput) { 'Verbose' } else { 'Compact (AI-Friendly)' })" -ForegroundColor Gray

# 1. Web Frontend Vitest Component Tests
Write-Host "==> Running Web Frontend Component Tests (Vitest)..." -ForegroundColor Yellow
Invoke-CompactTestCommand -TestType "Web Frontend (Vitest)" -VerboseOutput:$VerboseOutput -Action {
    Push-Location $webRoot
    try {
        $global:LASTEXITCODE = 0
        if ($IncludeE2e) {
            npx vitest run --reporter=dot
            if ($LASTEXITCODE -ne 0) {
                throw "Vitest unit tests failed with exit code $LASTEXITCODE."
            }
            npx playwright test --reporter=dot
        }
        else {
            npx vitest run --reporter=dot
        }

        if ($LASTEXITCODE -ne 0) {
            throw "Web frontend tests failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

# 2. .NET Solution Unit & Integration Tests
Write-Host "==> Running .NET Solution Tests (xUnit)..." -ForegroundColor Yellow
Invoke-CompactTestCommand -TestType ".NET Solution (xUnit)" -VerboseOutput:$VerboseOutput -Action {
    $global:LASTEXITCODE = 0

    $dotnetTestArgs = @(
        "test",
        $solution,
        "-m:1"
    )

    if ($Fast -or (-not $IncludeIntegration)) {
        # By default (or in fast mode), run all tests except the slow PopulatedWorkflow integration test
        if ([string]::IsNullOrWhiteSpace($Filter)) {
            $Filter = "FullyQualifiedName!~PopulatedWorkflow"
        } else {
            $Filter = "($Filter)&(FullyQualifiedName!~PopulatedWorkflow)"
        }
    }

    $dotnetTestArgs += @(
        "--configuration", $Configuration,
        "--nologo",
        "--logger", "console;verbosity=minimal"
    )

    if (-not [string]::IsNullOrWhiteSpace($Filter)) {
        $dotnetTestArgs += "--filter", $Filter
    }

    $dotnetTestArgs += @("--", "xUnit.ParallelizeTestCollections=false")

    dotnet @dotnetTestArgs
    if ($LASTEXITCODE -ne 0) {
        throw ".NET tests failed with exit code $LASTEXITCODE."
    }
}

Write-Host "All test suites completed successfully." -ForegroundColor Green


