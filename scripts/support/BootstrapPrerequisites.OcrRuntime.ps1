function Get-OcrCpuRuntimeSelection {
    param(
        [object]$Manifest,
        [string]$Detail = "Runtime OCR CPU selezionato."
    )

    $requirementsFile = "requirements.txt"
    $runtimeName = "cpu"
    if ($Manifest -and $Manifest.runtimeTargets) {
        $cpuTarget = @($Manifest.runtimeTargets | Where-Object { $_.resolvedRuntime -eq "cpu" } | Select-Object -First 1)
        if ($cpuTarget.Count -gt 0 -and $cpuTarget[0].requirementsFile) {
            $requirementsFile = [string]$cpuTarget[0].requirementsFile
        }
    }

    return [pscustomobject]@{
        RuntimeName = $runtimeName
        RequirementsFile = $requirementsFile
        RequirementsPath = Join-Path $ocrRoot $requirementsFile
        Device = "cpu"
        IsNvidia = $false
        Detail = $Detail
        PaddlePackage = "paddlepaddle"
        PaddleVersion = "3.3.1"
        CudnnPackage = $null
        CudnnVersion = $null
    }
}

function ConvertTo-OcrDriverVersion {
    param([string]$Text)

    if ($Text -match '(\d+)(?:\.(\d+))?(?:\.(\d+))?') {
        $minor = if ($Matches[2]) { [int]$Matches[2] } else { 0 }
        $build = if ($Matches[3]) { [int]$Matches[3] } else { 0 }
        return [version]::new([int]$Matches[1], $minor, $build)
    }

    return $null
}

function Get-OcrObjectPropertyValue {
    param(
        [object]$Object,
        [string]$Name
    )

    if (-not $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if (-not $property) {
        return $null
    }

    return $property.Value
}

function Get-OcrNvidiaRuntimeSelection {
    param([object]$Manifest)

    $nvidiaSmi = Get-Command nvidia-smi -ErrorAction SilentlyContinue
    if (-not $nvidiaSmi) {
        return Get-OcrCpuRuntimeSelection -Manifest $Manifest -Detail "NVIDIA non rilevata: nvidia-smi non trovato. Bootstrap OCR usera il runtime CPU."
    }

    try {
        $nvidiaOutput = (& $nvidiaSmi.Source --query-gpu=driver_version,name --format=csv,noheader 2>&1 | Out-String).Trim()
    }
    catch {
        return Get-OcrCpuRuntimeSelection -Manifest $Manifest -Detail "NVIDIA non utilizzabile: nvidia-smi non ha completato la verifica. Bootstrap OCR usera il runtime CPU."
    }

    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($nvidiaOutput)) {
        return Get-OcrCpuRuntimeSelection -Manifest $Manifest -Detail "NVIDIA non utilizzabile: nvidia-smi non ha completato la verifica. Bootstrap OCR usera il runtime CPU."
    }

    $firstLine = ($nvidiaOutput -split "(`r`n|`n|`r)" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1)
    $driverVersion = ConvertTo-OcrDriverVersion -Text $firstLine
    if (-not $driverVersion) {
        return Get-OcrCpuRuntimeSelection -Manifest $Manifest -Detail "NVIDIA rilevata, ma versione driver non leggibile. Bootstrap OCR usera il runtime CPU."
    }

    $displayName = $firstLine
    $commaIndex = $firstLine.IndexOf(",")
    if ($commaIndex -ge 0 -and $commaIndex -lt ($firstLine.Length - 1)) {
        $displayName = $firstLine.Substring($commaIndex + 1).Trim()
    }

    if (-not $Manifest -or -not $Manifest.runtimeTargets) {
        return Get-OcrCpuRuntimeSelection -Manifest $Manifest -Detail "Manifest OCR NVIDIA non disponibile. Bootstrap OCR usera il runtime CPU."
    }

    $nvidiaTargets = @(
        $Manifest.runtimeTargets |
            Where-Object { $_.target -eq "nvidia" -and $_.minimumWindowsDriver -and $_.requirementsFile } |
            Sort-Object { ConvertTo-OcrDriverVersion -Text ([string]$_.minimumWindowsDriver) } -Descending
    )

    foreach ($target in $nvidiaTargets) {
        $minimumDriver = ConvertTo-OcrDriverVersion -Text ([string]$target.minimumWindowsDriver)
        if (-not $minimumDriver -or $driverVersion -lt $minimumDriver) {
            continue
        }

        $requirementsFile = [string]$target.requirementsFile
        $requirementsPath = Join-Path $ocrRoot $requirementsFile
        if (-not (Test-Path -LiteralPath $requirementsPath -PathType Leaf)) {
            Add-Warning "OCR NVIDIA $($target.resolvedRuntime) saltato: $requirementsFile non trovato."
            continue
        }

        $cudnnPackage = Get-OcrObjectPropertyValue -Object $target -Name "cudnnPackage"
        $cudnnVersion = Get-OcrObjectPropertyValue -Object $target -Name "cudnnVersion"

        return [pscustomobject]@{
            RuntimeName = [string]$target.resolvedRuntime
            RequirementsFile = $requirementsFile
            RequirementsPath = $requirementsPath
            Device = "gpu"
            IsNvidia = $true
            Detail = "NVIDIA $displayName con driver $driverVersion compatibile con $($target.resolvedRuntime)."
            PaddlePackage = [string]$target.paddlePackage
            PaddleVersion = [string]$target.paddleVersion
            CudnnPackage = if ($cudnnPackage) { [string]$cudnnPackage } else { $null }
            CudnnVersion = if ($cudnnVersion) { [string]$cudnnVersion } else { $null }
        }
    }

    return Get-OcrCpuRuntimeSelection -Manifest $Manifest -Detail "Driver NVIDIA $driverVersion sotto il minimo supportato per PaddleOCR GPU Windows. Bootstrap OCR usera il runtime CPU."
}

function Test-OcrBenignNativeOutputLine {
    param([object]$Line)

    $text = [string]$Line
    return $text.Contains("No ccache found", [System.StringComparison]::OrdinalIgnoreCase) -or
        $text.Contains("extension_utils.py", [System.StringComparison]::OrdinalIgnoreCase) -or
        $text.Contains("warnings.warn(warning_message)", [System.StringComparison]::OrdinalIgnoreCase) -or
        $text.Contains("INFORMAZIONI: impossibile trovare file", [System.StringComparison]::OrdinalIgnoreCase) -or
        $text.Contains("criteri di ricerca indicati", [System.StringComparison]::OrdinalIgnoreCase)
}

function Invoke-OcrNativeCaptured {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,
        [Parameter()]
        [string[]]$Arguments = @(),
        [switch]$Quiet
    )

    Push-Location $repoRoot
    try {
        $outputItems = @(& $FilePath @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    $visibleLines = @(
        $outputItems |
            ForEach-Object { [string]$_ } |
            Where-Object { -not (Test-OcrBenignNativeOutputLine -Line $_) }
    )

    if ($exitCode -ne 0) {
        foreach ($line in $visibleLines) {
            Write-Host $line
        }

        throw "'$FilePath $($Arguments -join ' ')' failed with exit code $exitCode."
    }

    if (-not $Quiet) {
        foreach ($line in $visibleLines) {
            Write-Host $line
        }
    }

    return ($visibleLines -join [System.Environment]::NewLine)
}
