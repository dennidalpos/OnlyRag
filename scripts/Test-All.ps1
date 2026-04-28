#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest

$repoRoot   = Resolve-Path (Join-Path $PSScriptRoot "..")
$solution   = Join-Path $repoRoot "OnlyRag.sln"
$webProject = Join-Path $repoRoot "src\OnlyRag.Web"

$steps  = [System.Collections.Generic.List[pscustomobject]]::new()
$failed = $false

function Invoke-Step {
    param([string]$Name, [scriptblock]$Action)

    Write-Host ""
    Write-Host "--- $Name ---" -ForegroundColor Cyan

    try {
        & $Action
        $exitCode = $LASTEXITCODE
    }
    catch {
        Write-Host "ECCEZIONE: $_" -ForegroundColor Red
        $exitCode = 1
    }

    $ok = ($exitCode -eq 0)
    $steps.Add([pscustomobject]@{ Name = $Name; Ok = $ok })
    if (-not $ok) { $script:failed = $true }
}

Invoke-Step "dotnet test ($Configuration)" {
    dotnet test $solution --configuration $Configuration --logger "console;verbosity=minimal"
}

Invoke-Step "npm typecheck (src/OnlyRag.Web)" {
    Push-Location $webProject
    try { npm run typecheck }
    finally { Pop-Location }
}

Write-Host ""
Write-Host "=== RIEPILOGO ===" -ForegroundColor Cyan
foreach ($step in $steps) {
    $label = if ($step.Ok) { "OK  " } else { "FAIL" }
    $color = if ($step.Ok) { "Green" } else { "Red" }
    Write-Host "  [$label]  $($step.Name)" -ForegroundColor $color
}
Write-Host ""

if ($failed) {
    Write-Host "FALLITO — uno o piu passi non superati." -ForegroundColor Red
    exit 1
}

Write-Host "TUTTI I PASSI SUPERATI." -ForegroundColor Green
exit 0
