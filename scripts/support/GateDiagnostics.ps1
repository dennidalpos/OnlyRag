#requires -Version 5.1

function Format-GateDuration {
    param(
        [Parameter(Mandatory)]
        [TimeSpan]$Elapsed
    )

    if ($Elapsed.TotalMinutes -ge 1) {
        return "{0:mm\:ss\.fff}" -f $Elapsed
    }

    return "{0:ss\.fff}s" -f $Elapsed
}

function Invoke-GateStep {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [scriptblock]$Action
    )

    Write-Host ""
    Write-Host "==> $Name" -ForegroundColor Cyan

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $global:LASTEXITCODE = 0
    try {
        & $Action
        $exitCode = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
        if ($exitCode -ne 0) {
            throw "$Name failed with exit code $exitCode."
        }

        $stopwatch.Stop()
        $script:GateResults.Add([pscustomobject]@{
            Name = $Name
            Status = "passed"
            Duration = $stopwatch.Elapsed
            Error = $null
        })
        Write-Host "PASS $Name ($(Format-GateDuration -Elapsed $stopwatch.Elapsed))" -ForegroundColor Green
    }
    catch {
        $stopwatch.Stop()
        $message = $_.Exception.Message
        $script:GateFailures.Add([pscustomobject]@{
            Name = $Name
            Duration = $stopwatch.Elapsed
            Error = $message
        })
        $script:GateResults.Add([pscustomobject]@{
            Name = $Name
            Status = "failed"
            Duration = $stopwatch.Elapsed
            Error = $message
        })

        Write-Host "FAIL $Name ($(Format-GateDuration -Elapsed $stopwatch.Elapsed))" -ForegroundColor Red
        Write-Host "  $message" -ForegroundColor Red

        if (-not $ContinueOnError) {
            Write-GateSummary
            Write-Host ""
            Write-Host "Gate arrestato immediatamente al primo errore nel passaggio '$Name'." -ForegroundColor Red
            exit 1
        }
    }
}

function Assert-CommandAvailable {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string]$InstallHint
    )

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if (-not $command) {
        throw "$Name was not found. $InstallHint"
    }

    Write-Host "  ${Name}: $($command.Source)"
}

function Write-GateToolVersion {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if (-not $command) {
        Write-Host "  ${Name}: not found" -ForegroundColor Yellow
        return
    }

    try {
        $global:LASTEXITCODE = 0
        $output = & $command.Source @Arguments 2>&1 | Out-String
        $version = ($output -split "\r?\n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1)
        if ([string]::IsNullOrWhiteSpace($version)) {
            $version = "version unavailable"
        }

        Write-Host "  ${Name} version: $version"
    }
    catch {
        Write-Host "  ${Name} version: unavailable ($($_.Exception.Message))" -ForegroundColor Yellow
    }
    finally {
        $global:LASTEXITCODE = 0
    }
}

function Write-GateSummary {
    $gateStopwatch.Stop()
    Write-Host ""
    Write-Host "Repository gate summary" -ForegroundColor Cyan
    Write-Host "Total duration: $(Format-GateDuration -Elapsed $gateStopwatch.Elapsed)"

    foreach ($result in $script:GateResults) {
        $color = if ($result.Status -eq "passed") { "Green" } else { "Red" }
        Write-Host ("  {0,-8} {1} ({2})" -f $result.Status.ToUpperInvariant(), $result.Name, (Format-GateDuration -Elapsed $result.Duration)) -ForegroundColor $color
    }

    if ($script:GateFailures.Count -eq 0) {
        return
    }

    Write-Host ""
    Write-Host "Detected errors:" -ForegroundColor Red
    foreach ($failure in $script:GateFailures) {
        Write-Host "  - $($failure.Name): $($failure.Error)" -ForegroundColor Red
    }
}

function Get-JsonPropertyValue {
    param(
        [object]$Object,

        [Parameter(Mandatory)]
        [string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Format-CompactTestSummary {
    param(
        [string]$OutputText
    )

    if ([string]::IsNullOrWhiteSpace($OutputText)) {
        return "Passed"
    }

    $lines = $OutputText -split "\r?\n"
    
    $dotnetMatch = $lines | Where-Object { $_ -match "(Passed|Failed)!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+)" } | Select-Object -First 1
    if ($dotnetMatch) {
        return $dotnetMatch.Trim()
    }

    $vitestMatch = $lines | Where-Object { $_ -match "Tests\s+\d+\s+passed" -or $_ -match "Test Files\s+\d+\s+passed" } | Select-Object -First 5
    if ($vitestMatch) {
        return ($vitestMatch -join " | ").Trim()
    }

    return "Passed"
}

function Format-CompactTestFailure {
    param(
        [string]$OutputText
    )

    if ([string]::IsNullOrWhiteSpace($OutputText)) {
        return "Test run failed with unknown error."
    }

    $lines = $OutputText -split "\r?\n"
    $relevantLines = New-Object System.Collections.Generic.List[string]

    foreach ($line in $lines) {
        if ($line -match "Failed\s+!|FAIL\s+|Error:|Exception:|Expected:|Received:|at\s+[A-Za-z0-9_.]+\(|AssertionError|\[FAIL\]") {
            if ($line -notmatch "Determining projects to restore|Build completed|Restore completed") {
                $relevantLines.Add($line.TrimEnd())
            }
        }
    }

    if ($relevantLines.Count -eq 0) {
        $nonEmpty = $lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        $relevantLines.AddRange(($nonEmpty | Select-Object -Last 15))
    }

    return ($relevantLines | Select-Object -First 25) -join "`n"
}

function Invoke-CompactTestCommand {
    param(
        [Parameter(Mandatory)]
        [string]$TestType,

        [Parameter(Mandatory)]
        [scriptblock]$Action,

        [switch]$VerboseOutput
    )

    if ($VerboseOutput) {
        Write-Host "  [Verbose Mode] Executing $TestType..." -ForegroundColor Yellow
        & $Action
        return
    }

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $global:LASTEXITCODE = 0
    
    $output = & {
        try {
            & $Action 2>&1
        }
        catch {
            $_
        }
    } | Out-String

    $stopwatch.Stop()
    $exitCode = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }

    if ($exitCode -eq 0) {
        $summary = Format-CompactTestSummary -OutputText $output
        $durationText = Format-GateDuration -Elapsed $stopwatch.Elapsed
        Write-Host "  [PASS] $TestType ($durationText) - $summary" -ForegroundColor Green
    }
    else {
        $durationText = Format-GateDuration -Elapsed $stopwatch.Elapsed
        Write-Host "  [FAIL] $TestType ($durationText)" -ForegroundColor Red
        $failureDetails = Format-CompactTestFailure -OutputText $output
        Write-Host "--- Failure Traceback (Compact) ---" -ForegroundColor Red
        Write-Host $failureDetails -ForegroundColor Red
        Write-Host "----------------------------------" -ForegroundColor Red
        throw "$TestType failed with exit code $exitCode."
    }
}

