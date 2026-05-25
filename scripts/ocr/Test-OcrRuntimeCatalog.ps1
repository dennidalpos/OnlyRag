#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path "artifacts") "ocr-runtime-catalog\ocr-runtime-catalog.json"),
    [string[]]$PythonCommands = @("python")
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$manifestPath = Join-Path $PSScriptRoot "runtime-manifest.json"
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json

$knownCatalog = @(
    [pscustomobject]@{ Runtime = "cuda130"; Url = "https://www.paddlepaddle.org.cn/packages/stable/cu130/"; Package = "paddlepaddle-gpu"; Version = "3.3.0" },
    [pscustomobject]@{ Runtime = "cuda129"; Url = "https://www.paddlepaddle.org.cn/packages/stable/cu129/"; Package = "paddlepaddle-gpu"; Version = "3.3.0" },
    [pscustomobject]@{ Runtime = "cuda126"; Url = "https://www.paddlepaddle.org.cn/packages/stable/cu126/"; Package = "paddlepaddle-gpu"; Version = "3.3.0" },
    [pscustomobject]@{ Runtime = "cuda118"; Url = "https://www.paddlepaddle.org.cn/packages/stable/cu118/"; Package = "paddlepaddle-gpu"; Version = "3.3.0" }
)

function Compare-VersionString {
    param(
        [string]$Left,
        [string]$Right
    )

    $normalizedLeft = $Left -replace "\.x$", ".999"
    $normalizedRight = $Right -replace "\.x$", ".999"
    return ([version]$normalizedLeft).CompareTo([version]$normalizedRight)
}

function Test-PipDryRun {
    param(
        [object]$Candidate,
        [object]$ManifestTarget,
        [string]$PythonCommand
    )

    $python = Get-Command $PythonCommand -ErrorAction SilentlyContinue
    if (-not $python) {
        return [pscustomobject]@{
            python = $PythonCommand
            pythonVersion = $null
            skipped = $true
            installable = $null
            detail = "python command not available"
        }
    }

    $versionOutput = @(& $python.Source -c "import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}')" 2>&1)
    $versionText = (($versionOutput | ForEach-Object { [string]$_ }) -join [Environment]::NewLine).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($versionText)) {
        return [pscustomobject]@{
            python = $PythonCommand
            pythonVersion = $null
            skipped = $true
            installable = $null
            detail = if ([string]::IsNullOrWhiteSpace($versionText)) { "python version check failed" } else { $versionText }
        }
    }

    $minimumPython = [string]$ManifestTarget.supportedPythonMin
    $maximumPython = [string]$ManifestTarget.supportedPythonMax
    $majorMinor = ([version]$versionText).ToString(2)
    if (
        -not [string]::IsNullOrWhiteSpace($minimumPython) -and
        (Compare-VersionString -Left $majorMinor -Right $minimumPython) -lt 0
    ) {
        return [pscustomobject]@{
            python = $PythonCommand
            pythonVersion = $versionText
            skipped = $true
            installable = $null
            detail = "python $majorMinor is below manifest minimum $minimumPython"
        }
    }

    if (
        -not [string]::IsNullOrWhiteSpace($maximumPython) -and
        (Compare-VersionString -Left $majorMinor -Right $maximumPython) -gt 0
    ) {
        return [pscustomobject]@{
            python = $PythonCommand
            pythonVersion = $versionText
            skipped = $true
            installable = $null
            detail = "python $majorMinor is above manifest maximum $maximumPython"
        }
    }

    $requirement = "$($Candidate.Package)==$($Candidate.Version)"
    $output = @(& $python.Source -m pip install --dry-run --disable-pip-version-check --only-binary=:all: --no-deps --extra-index-url $Candidate.Url $requirement 2>&1)
    $exitCode = $LASTEXITCODE
    $text = ($output | ForEach-Object { [string]$_ }) -join [Environment]::NewLine
    return [pscustomobject]@{
        python = $PythonCommand
        pythonVersion = $versionText
        skipped = $false
        installable = $exitCode -eq 0
        detail = if ([string]::IsNullOrWhiteSpace($text)) { "no output" } else { $text.Trim() }
    }
}

$results = foreach ($candidate in $knownCatalog) {
    $manifestTarget = @($manifest.runtimeTargets | Where-Object { $_.resolvedRuntime -eq $candidate.Runtime } | Select-Object -First 1)
    $sourceReachable = $false
    $sourceStatus = $null
    try {
        $response = Invoke-WebRequest -Uri $candidate.Url -Method Head -TimeoutSec 20
        $sourceReachable = [int]$response.StatusCode -ge 200 -and [int]$response.StatusCode -lt 400
        $sourceStatus = [int]$response.StatusCode
    }
    catch {
        $sourceStatus = $_.Exception.Message
    }

    $targetForDryRun = if ($manifestTarget.Count -gt 0) {
        $manifestTarget[0]
    }
    else {
        [pscustomobject]@{ supportedPythonMin = "3.10"; supportedPythonMax = "3.13" }
    }
    $dryRuns = @($PythonCommands | ForEach-Object {
        Test-PipDryRun -Candidate $candidate -ManifestTarget $targetForDryRun -PythonCommand $_
    })
    [pscustomobject]@{
        runtime = $candidate.Runtime
        sourceUrl = $candidate.Url
        sourceReachable = $sourceReachable
        sourceStatus = $sourceStatus
        expectedPackage = $candidate.Package
        expectedVersion = $candidate.Version
        manifestPresent = $manifestTarget.Count -gt 0
        dryRunInstallable = @($dryRuns | Where-Object { $_.installable -eq $true }).Count -gt 0
        pipDryRuns = $dryRuns
    }
}

$updateRecommended = @($results | Where-Object {
    ($_.sourceReachable -eq $true -or $_.dryRunInstallable -eq $true) -and -not $_.manifestPresent
}).Count -gt 0

$report = [pscustomobject]@{
    generatedAt = (Get-Date -Format "o")
    manifestPath = [System.IO.Path]::GetRelativePath($repoRoot, $manifestPath)
    updateRecommended = $updateRecommended
    results = @($results)
}

$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
$report | ConvertTo-Json -Depth 10

if ($updateRecommended) {
    exit 2
}
