param(
    [ValidateSet("auto", "cpu", "nvidia")]
    [string]$RuntimeTarget = "auto",

    [switch]$FailOnError
)

$ErrorActionPreference = "Stop"

$script:OcrRoot = $PSScriptRoot
$script:BridgePath = Join-Path $script:OcrRoot "paddle_ocr_bridge.py"
$script:ManifestPath = Join-Path $script:OcrRoot "runtime-manifest.json"
$script:LocalRoot = Join-Path $env:LOCALAPPDATA "OnlyRag"
$script:InstallRoot = Join-Path $script:LocalRoot "ocr-python"
$script:VenvPath = Join-Path $script:InstallRoot ".venv"
$script:VenvPython = Join-Path $script:VenvPath "Scripts\python.exe"
$script:LogDir = Join-Path $script:LocalRoot "logs"
$script:LogPath = Join-Path $script:LogDir "ocr-setup-install.log"

function Write-OcrSetupLog {
    param([Parameter(Mandatory)][string]$Message)

    New-Item -ItemType Directory -Force -Path $script:LogDir | Out-Null
    $line = "{0} {1}" -f (Get-Date -Format "o"), $Message
    Add-Content -LiteralPath $script:LogPath -Value $line -Encoding UTF8
    Write-Host $Message
}

function ConvertTo-OcrVersion {
    param([string]$Text)

    if ($Text -match '(\d+)(?:\.(\d+))?(?:\.(\d+))?') {
        $minor = if ($Matches[2]) { [int]$Matches[2] } else { 0 }
        $build = if ($Matches[3]) { [int]$Matches[3] } else { 0 }
        return [version]::new([int]$Matches[1], $minor, $build)
    }

    return $null
}

function Test-OcrPythonVersion {
    param([version]$Version)

    return $Version.Major -eq 3 -and $Version.Minor -ge 10 -and $Version.Minor -le 13
}

function Invoke-OcrSetupProcess {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$Arguments = @(),
        [switch]$Quiet
    )

    Write-OcrSetupLog "Running: $FilePath $($Arguments -join ' ')"
    $output = @(& $FilePath @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $text = ($output | ForEach-Object { [string]$_ }) -join [Environment]::NewLine
    if (-not $Quiet -and -not [string]::IsNullOrWhiteSpace($text)) {
        Write-OcrSetupLog $text
    }

    if ($exitCode -ne 0) {
        throw "'$FilePath $($Arguments -join ' ')' failed with exit code $exitCode. $text"
    }

    return $text
}

function Get-OcrPythonCommand {
    $candidates = New-Object System.Collections.Generic.List[object]
    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($python) {
        $candidates.Add([pscustomobject]@{ FilePath = $python.Source; Arguments = @(); Name = "python" })
    }

    $py = Get-Command py -ErrorAction SilentlyContinue
    if ($py) {
        foreach ($minor in @(13, 12, 11, 10)) {
            $candidates.Add([pscustomobject]@{ FilePath = $py.Source; Arguments = @("-3.$minor"); Name = "py -3.$minor" })
        }
    }

    foreach ($candidate in $candidates) {
        try {
            $versionText = Invoke-OcrSetupProcess -FilePath $candidate.FilePath -Arguments (@($candidate.Arguments) + @("--version")) -Quiet
            $version = ConvertTo-OcrVersion -Text $versionText
            if ($version -and (Test-OcrPythonVersion -Version $version)) {
                return [pscustomobject]@{
                    FilePath = $candidate.FilePath
                    Arguments = @($candidate.Arguments)
                    VersionText = $versionText.Trim()
                    Name = $candidate.Name
                }
            }
        }
        catch {
            continue
        }
    }

    throw "Python 3.10 through 3.13 was not found. Install compatible Python for OCR preinstall."
}

function Get-OcrNvidiaSmiPath {
    $candidates = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($env:WINDIR)) {
        $candidates.Add((Join-Path $env:WINDIR "Sysnative\nvidia-smi.exe"))
        $candidates.Add((Join-Path $env:WINDIR "System32\nvidia-smi.exe"))
    }

    $command = Get-Command nvidia-smi -ErrorAction SilentlyContinue
    if ($command) {
        $candidates.Add($command.Source)
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    return $null
}

function Get-OcrCpuRuntime {
    param([object]$Manifest, [string]$Detail)

    $target = @($Manifest.runtimeTargets | Where-Object { $_.resolvedRuntime -eq "cpu" } | Select-Object -First 1)
    $requirementsFile = if ($target.Count -gt 0) { [string]$target[0].requirementsFile } else { "requirements-cpu.txt" }
    return [pscustomobject]@{
        RuntimeName = "cpu"
        RequirementsFile = $requirementsFile
        RequirementsPath = Join-Path $script:OcrRoot $requirementsFile
        Device = "cpu"
        Detail = $Detail
    }
}

function Resolve-OcrRuntime {
    param([object]$Manifest)

    if ($RuntimeTarget -eq "cpu") {
        return Get-OcrCpuRuntime -Manifest $Manifest -Detail "CPU runtime requested by setup."
    }

    $nvidiaSmi = Get-OcrNvidiaSmiPath
    if (-not $nvidiaSmi) {
        if ($RuntimeTarget -eq "nvidia") {
            throw "NVIDIA runtime requested, but nvidia-smi.exe was not found."
        }
        return Get-OcrCpuRuntime -Manifest $Manifest -Detail "NVIDIA not detected; setup selected CPU runtime."
    }

    $nvidiaOutput = Invoke-OcrSetupProcess -FilePath $nvidiaSmi -Arguments @("--query-gpu=driver_version,name", "--format=csv,noheader") -Quiet
    $firstLine = @($nvidiaOutput -split "(`r`n|`n|`r)" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1)
    if ($firstLine.Count -eq 0) {
        if ($RuntimeTarget -eq "nvidia") {
            throw "nvidia-smi did not return GPU driver information."
        }
        return Get-OcrCpuRuntime -Manifest $Manifest -Detail "NVIDIA detected but nvidia-smi returned no GPU data; setup selected CPU runtime."
    }

    $driverVersion = ConvertTo-OcrVersion -Text $firstLine[0]
    if (-not $driverVersion) {
        if ($RuntimeTarget -eq "nvidia") {
            throw "NVIDIA driver version was not readable from nvidia-smi."
        }
        return Get-OcrCpuRuntime -Manifest $Manifest -Detail "NVIDIA detected but driver version was not readable; setup selected CPU runtime."
    }

    $targets = @(
        $Manifest.runtimeTargets |
            Where-Object { $_.target -eq "nvidia" -and $_.minimumWindowsDriver -and $_.requirementsFile } |
            Sort-Object @{ Expression = { ConvertTo-OcrVersion -Text ([string]$_.minimumWindowsDriver) }; Descending = $true }
    )

    foreach ($target in $targets) {
        $minimumDriver = ConvertTo-OcrVersion -Text ([string]$target.minimumWindowsDriver)
        if (-not $minimumDriver -or $driverVersion -lt $minimumDriver) {
            continue
        }

        $requirementsPath = Join-Path $script:OcrRoot ([string]$target.requirementsFile)
        if (-not (Test-Path -LiteralPath $requirementsPath -PathType Leaf)) {
            continue
        }

        return [pscustomobject]@{
            RuntimeName = [string]$target.resolvedRuntime
            RequirementsFile = [string]$target.requirementsFile
            RequirementsPath = $requirementsPath
            Device = "gpu"
            Detail = "NVIDIA driver $driverVersion selected $($target.resolvedRuntime)."
        }
    }

    if ($RuntimeTarget -eq "nvidia") {
        throw "NVIDIA driver $driverVersion is below the minimum supported PaddleOCR GPU runtime."
    }
    return Get-OcrCpuRuntime -Manifest $Manifest -Detail "NVIDIA driver $driverVersion is below the supported GPU runtime; setup selected CPU runtime."
}

function Test-OcrRuntimePrepared {
    param([object]$Runtime)

    $stampPath = Join-Path $script:VenvPath ".requirements-stamp"
    if (-not (Test-Path -LiteralPath $script:VenvPython -PathType Leaf) -or -not (Test-Path -LiteralPath $stampPath -PathType Leaf)) {
        return $false
    }

    try {
        $stamp = Get-Content -Raw -LiteralPath $stampPath | ConvertFrom-Json
        if ($stamp.runtimeName -ne $Runtime.RuntimeName -or $stamp.requirementsFile -ne $Runtime.RequirementsFile) {
            return $false
        }

        Invoke-OcrSetupProcess -FilePath $script:VenvPython -Arguments @($script:BridgePath, "--mode", "check", "--device", $Runtime.Device) -Quiet | Out-Null
        return $true
    }
    catch {
        Write-OcrSetupLog "Existing OCR runtime check failed: $($_.Exception.Message)"
        return $false
    }
}

function Initialize-OcrRuntime {
    if (-not (Test-Path -LiteralPath $script:BridgePath -PathType Leaf)) {
        throw "OCR bridge not found: $script:BridgePath"
    }
    if (-not (Test-Path -LiteralPath $script:ManifestPath -PathType Leaf)) {
        throw "OCR runtime manifest not found: $script:ManifestPath"
    }

    $manifest = Get-Content -Raw -LiteralPath $script:ManifestPath | ConvertFrom-Json
    $runtime = Resolve-OcrRuntime -Manifest $manifest
    Write-OcrSetupLog "OCR setup selected runtime $($runtime.RuntimeName). $($runtime.Detail)"

    if (Test-OcrRuntimePrepared -Runtime $runtime) {
        Write-OcrSetupLog "OCR runtime already prepared for $($runtime.RuntimeName)."
        return
    }

    $python = Get-OcrPythonCommand
    Write-OcrSetupLog "Python selected for OCR: $($python.VersionText) via $($python.Name)."

    New-Item -ItemType Directory -Force -Path $script:InstallRoot | Out-Null
    if (Test-Path -LiteralPath $script:VenvPython -PathType Leaf) {
        $venvVersionText = Invoke-OcrSetupProcess -FilePath $script:VenvPython -Arguments @("--version") -Quiet
        $venvVersion = ConvertTo-OcrVersion -Text $venvVersionText
        if (-not $venvVersion -or -not (Test-OcrPythonVersion -Version $venvVersion)) {
            $installFullPath = [System.IO.Path]::GetFullPath($script:InstallRoot).TrimEnd("\") + "\"
            $venvFullPath = [System.IO.Path]::GetFullPath($script:VenvPath)
            if (-not $venvFullPath.StartsWith($installFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to remove OCR venv outside install root: $venvFullPath"
            }
            Remove-Item -LiteralPath $script:VenvPath -Recurse -Force
        }
    }

    if (-not (Test-Path -LiteralPath $script:VenvPython -PathType Leaf)) {
        Invoke-OcrSetupProcess -FilePath $python.FilePath -Arguments (@($python.Arguments) + @("-m", "venv", $script:VenvPath)) | Out-Null
    }

    Invoke-OcrSetupProcess -FilePath $script:VenvPython -Arguments @("-m", "pip", "install", "--upgrade", "pip", "--disable-pip-version-check") | Out-Null
    Invoke-OcrSetupProcess -FilePath $script:VenvPython -Arguments @("-m", "pip", "uninstall", "-y", "paddlepaddle", "paddlepaddle-gpu") | Out-Null
    Invoke-OcrSetupProcess -FilePath $script:VenvPython -Arguments @("-m", "pip", "install", "--upgrade", "-r", $runtime.RequirementsPath, "--disable-pip-version-check") | Out-Null
    Invoke-OcrSetupProcess -FilePath $script:VenvPython -Arguments @($script:BridgePath, "--mode", "check", "--device", $runtime.Device) | Out-Null

    $stamp = [pscustomobject]@{
        runtimeName = $runtime.RuntimeName
        requirementsFile = $runtime.RequirementsFile
        updatedAt = (Get-Date -Format "o")
    } | ConvertTo-Json -Compress
    [System.IO.File]::WriteAllText((Join-Path $script:VenvPath ".requirements-stamp"), $stamp)
    Write-OcrSetupLog "OCR runtime prepared for $($runtime.RuntimeName)."
}

try {
    Initialize-OcrRuntime
    exit 0
}
catch {
    Write-OcrSetupLog "OCR setup preinstall did not complete: $($_.Exception.Message)"
    if ($FailOnError) {
        exit 1
    }
    exit 0
}
