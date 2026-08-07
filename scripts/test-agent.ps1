<#
.SYNOPSIS
    Agent fast-mode test runner (AGENTS.md compliant).
    Runs PASS/FAIL summary only. No full integration suite.

.PARAMETER Full
    Switch to run the complete test suite (manual/debugging mode only).
#>
param([switch]$Full)

$env:ONLYRAG_TEST_ENVIRONMENT = "true"

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$script:failed = 0

function Invoke-DotnetTest {
    param([string]$Project, [string]$Filter = '')
    $testArgs = @(
        'test', $Project,
        '--configuration', 'Release',
        '--nologo',
        '--logger', 'console;verbosity=minimal',
        '-m:1'
    )
    if ($Filter) { $testArgs += '--filter'; $testArgs += $Filter }
    $testArgs += @('--', 'xUnit.ParallelizeTestCollections=false')
    $global:LASTEXITCODE = 0
    $buffer = [System.Collections.Generic.List[string]]::new()
    & dotnet @testArgs | ForEach-Object {
        $line = $_
        $buffer.Add($line)
        if ($line -match "Superato!|Fallito!|Superati:|non superati:|Passed!|Failed!") {
            Write-Host "  $line"
        }
    }
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAIL — Test suite failed: $Project" -ForegroundColor Red
        Write-Host "--- Failure Traceback ---" -ForegroundColor Red
        $startIdx = [Math]::Max(0, $buffer.Count - 15)
        for ($i = $startIdx; $i -lt $buffer.Count; $i++) {
            Write-Host $buffer[$i] -ForegroundColor Red
        }
        $script:failed++
    }
}

function Invoke-VitestTest {
    param([switch]$E2e)
    $global:LASTEXITCODE = 0
    Push-Location "$root\src\OnlyRag.Web"
    try {
        Write-Host "  Running Vitest (unit)..." -ForegroundColor Gray
        $output = & npx vitest run --reporter=dot 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) {
            Write-Host "FAIL — Vitest failed" -ForegroundColor Red
            Write-Host "--- Failure Traceback ---" -ForegroundColor Red
            Write-Host $output -ForegroundColor Red
            $script:failed++
            return
        }
        Write-Host "  Passed (Vitest)" -ForegroundColor Green

        if ($E2e) {
            Write-Host "  Running Playwright (E2E)..." -ForegroundColor Gray
            $output = & npx playwright test --reporter=dot 2>&1 | Out-String
            if ($LASTEXITCODE -ne 0) {
                Write-Host "FAIL — Playwright E2E failed" -ForegroundColor Red
                Write-Host "--- Failure Traceback ---" -ForegroundColor Red
                Write-Host $output -ForegroundColor Red
                $script:failed++
                return
            }
            Write-Host "  Passed (Playwright E2E)" -ForegroundColor Green
        }
    }
    catch {
        Write-Host "FAIL — Error running frontend tests: $_" -ForegroundColor Red
        $script:failed++
    }
    finally {
        Pop-Location
    }
}

Write-Host "`n=== Web Frontend Tests ===" -ForegroundColor Cyan
if ($Full) {
    Invoke-VitestTest -E2e
} else {
    Invoke-VitestTest
}

Write-Host "`n=== OnlyRag.Core.Tests ===" -ForegroundColor Cyan
Invoke-DotnetTest "$root\tests\OnlyRag.Core.Tests\OnlyRag.Core.Tests.csproj"

Write-Host "`n=== OnlyRag.Infrastructure.Tests ===" -ForegroundColor Cyan
Invoke-DotnetTest "$root\tests\OnlyRag.Infrastructure.Tests\OnlyRag.Infrastructure.Tests.csproj"

Write-Host "`n=== OnlyRag.Api.Tests ===" -ForegroundColor Cyan

# Fast mode: include only unit and lightweight integration tests.
# Excluded: PopulatedWorkflow (which starts the Kestrel backend)
$fastFilter = 'FullyQualifiedName~OnlyRag.Api.Tests.EndToEndIntegration'

if ($Full) {
    Write-Host "(FULL mode — running all Api.Tests)" -ForegroundColor Yellow
    Invoke-DotnetTest "$root\tests\OnlyRag.Api.Tests\OnlyRag.Api.Tests.csproj"
}
else {
    Invoke-DotnetTest "$root\tests\OnlyRag.Api.Tests\OnlyRag.Api.Tests.csproj" $fastFilter
}

Write-Host ""
if ($script:failed -eq 0) {
    Write-Host "ALL PASS" -ForegroundColor Green
} else {
    Write-Host "FAIL — $($script:failed) suite(s) failed" -ForegroundColor Red
    exit 1
}
