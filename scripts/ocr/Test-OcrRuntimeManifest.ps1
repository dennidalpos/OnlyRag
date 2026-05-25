#requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ocrRoot = $PSScriptRoot
$manifestPath = Join-Path $ocrRoot "runtime-manifest.json"

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "OCR runtime manifest not found: $manifestPath"
}

$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$targets = @($manifest.runtimeTargets)
if ($targets.Count -eq 0) {
    throw "OCR runtime manifest has no runtimeTargets."
}

$errors = New-Object System.Collections.Generic.List[string]
$resolvedNames = New-Object System.Collections.Generic.HashSet[string]

foreach ($target in $targets) {
    $resolvedRuntime = [string]$target.resolvedRuntime
    if ([string]::IsNullOrWhiteSpace($resolvedRuntime)) {
        $errors.Add("A runtime target is missing resolvedRuntime.")
        continue
    }

    if (-not $resolvedNames.Add($resolvedRuntime)) {
        $errors.Add("Duplicate resolvedRuntime '$resolvedRuntime'.")
    }

    $requirementsFile = [string]$target.requirementsFile
    if ([string]::IsNullOrWhiteSpace($requirementsFile)) {
        $errors.Add("$resolvedRuntime is missing requirementsFile.")
        continue
    }

    $requirementsPath = Join-Path $ocrRoot $requirementsFile
    if (-not (Test-Path -LiteralPath $requirementsPath -PathType Leaf)) {
        $errors.Add("$resolvedRuntime references missing requirements file: $requirementsFile.")
    }

    if ($target.target -eq "nvidia") {
        foreach ($requiredProperty in @("minimumWindowsDriver", "minimumComputeCapability", "sourceUrl")) {
            $propertyValue = $target.PSObject.Properties[$requiredProperty].Value
            if ([string]::IsNullOrWhiteSpace([string]$propertyValue)) {
                $errors.Add("$resolvedRuntime is missing $requiredProperty.")
            }
        }
    }
}

foreach ($requirementsPath in Get-ChildItem -LiteralPath $ocrRoot -Filter "requirements-nvidia-*.txt") {
    $isReferenced = $targets | Where-Object { $_.requirementsFile -eq $requirementsPath.Name } | Select-Object -First 1
    if (-not $isReferenced) {
        $errors.Add("$($requirementsPath.Name) is not referenced by runtime-manifest.json.")
    }
}

if ($errors.Count -gt 0) {
    Write-Host "OCR runtime manifest validation failed:" -ForegroundColor Red
    foreach ($errorItem in $errors) {
        Write-Host "  - $errorItem" -ForegroundColor Red
    }
    exit 1
}

Write-Host "OCR runtime manifest validation passed for $($targets.Count) target(s)."
