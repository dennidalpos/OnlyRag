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

function Test-OcrNvidiaSeries50 {
    param([string]$GpuName)

    return $GpuName -match '\bRTX\s+50\d{2}\b' -or $GpuName -match '\bRTX\s+5\d{3}\b'
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
        $nvidiaOutput = (& $nvidiaSmi.Source --query-gpu=driver_version,name,compute_cap --format=csv,noheader 2>&1 | Out-String).Trim()
        if ($LASTEXITCODE -ne 0) {
            $nvidiaOutput = (& $nvidiaSmi.Source --query-gpu=driver_version,name --format=csv,noheader 2>&1 | Out-String).Trim()
        }
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

    $parts = @($firstLine -split "," | ForEach-Object { $_.Trim() })
    $displayName = if ($parts.Count -ge 2) { $parts[1] } else { $firstLine }
    $computeCapability = if ($parts.Count -ge 3) { ConvertTo-OcrDriverVersion -Text $parts[2] } else { $null }

    if (Test-OcrNvidiaSeries50 -GpuName $displayName) {
        return Get-OcrCpuRuntimeSelection -Manifest $Manifest -Detail "NVIDIA $displayName rilevata, ma il supporto PaddlePaddle Windows per RTX serie 50 resta sperimentale/speciale. Bootstrap OCR usera il runtime CPU."
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
        $minimumCompute = ConvertTo-OcrDriverVersion -Text ([string]$target.minimumComputeCapability)
        if ($minimumCompute -and $computeCapability -and $computeCapability -lt $minimumCompute) {
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
    return $text.IndexOf("No ccache found", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $text.IndexOf("extension_utils.py", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $text.IndexOf("warnings.warn(warning_message)", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $text.IndexOf("INFORMAZIONI: impossibile trovare file", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $text.IndexOf("criteri di ricerca indicati", [System.StringComparison]::OrdinalIgnoreCase) -ge 0
}

function Test-OcrBenignPaddleUninstallOutput {
    param([string]$Text)

    return -not [string]::IsNullOrWhiteSpace($Text) -and
        $Text.IndexOf("Skipping paddlepaddle", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -and
        $Text.IndexOf("not installed", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -and
        $Text.IndexOf("ERROR:", [System.StringComparison]::OrdinalIgnoreCase) -lt 0
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
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $outputItems = @(& $FilePath @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
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
