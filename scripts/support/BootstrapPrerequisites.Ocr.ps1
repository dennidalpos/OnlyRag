function Get-OcrRuntimeManifest {
    if (-not (Test-Path -LiteralPath $ocrManifestPath -PathType Leaf)) {
        Add-Warning "OCR runtime manifest was not found. Falling back to requirements.txt only."
        return $null
    }

    return Get-Content -Raw -LiteralPath $ocrManifestPath | ConvertFrom-Json
}

function Test-OcrDiskSpace {
    param([object]$Manifest)

    if (-not $Manifest -or -not $Manifest.minimumFreeDiskBytes) {
        return
    }

    $driveRoot = [System.IO.Path]::GetPathRoot($localDataRoot)
    $drive = Get-PSDrive -Name $driveRoot.TrimEnd(":\") -ErrorAction SilentlyContinue
    if (-not $drive) {
        Add-Warning "Could not determine free disk space for OCR runtime at $localDataRoot."
        return
    }

    [int64]$requiredBytes = $Manifest.minimumFreeDiskBytes
    if ($drive.Free -lt $requiredBytes) {
        Add-Warning "OCR runtime may need at least $([math]::Round($requiredBytes / 1GB, 1)) GB free; available on $driveRoot is $([math]::Round($drive.Free / 1GB, 1)) GB."
    }
    else {
        Add-Verified "OCR runtime disk space check passed: $([math]::Round($drive.Free / 1GB, 1)) GB available."
    }
}

function Test-OcrSupportedPythonVersion {
    param([version]$Version)

    return (
        $Version.Major -eq 3 -and
        $Version.Minor -ge 10 -and
        $Version.Minor -le 13
    )
}

function Get-OcrPythonVersion {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,
        [string[]]$Arguments = @()
    )

    $versionArguments = @($Arguments) + @("--version")
    $versionText = (& $FilePath @versionArguments 2>&1 | Out-String).Trim()
    $version = ConvertTo-VersionOrNull $versionText
    if (-not $version) {
        return $null
    }

    return [pscustomobject]@{
        FilePath = $FilePath
        Arguments = @($Arguments)
        Version = $version
        VersionText = $versionText
    }
}

function Get-OcrPythonCommand {
    $candidates = [System.Collections.Generic.List[object]]::new()

    $pythonCommand = Get-Command python -ErrorAction SilentlyContinue
    if ($pythonCommand) {
        $candidates.Add([pscustomobject]@{
            FilePath = $pythonCommand.Source
            Arguments = @()
            DisplayName = "python"
        })
    }

    $pyCommand = Get-Command py -ErrorAction SilentlyContinue
    if ($pyCommand) {
        foreach ($minor in @(13, 12, 11, 10)) {
            $candidates.Add([pscustomobject]@{
                FilePath = $pyCommand.Source
                Arguments = @("-3.$minor")
                DisplayName = "py -3.$minor"
            })
        }
    }

    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $unsupported = [System.Collections.Generic.List[string]]::new()
    foreach ($candidate in $candidates) {
        $key = "$($candidate.FilePath)|$($candidate.Arguments -join ' ')"
        if (-not $seen.Add($key)) {
            continue
        }

        try {
            $detected = Get-OcrPythonVersion -FilePath $candidate.FilePath -Arguments $candidate.Arguments
            if (-not $detected) {
                continue
            }

            if (Test-OcrSupportedPythonVersion -Version $detected.Version) {
                return [pscustomobject]@{
                    FilePath = $detected.FilePath
                    Arguments = @($detected.Arguments)
                    Version = $detected.Version
                    VersionText = $detected.VersionText
                    DisplayName = $candidate.DisplayName
                }
            }

            $unsupported.Add("$($candidate.DisplayName) -> $($detected.VersionText)")
        }
        catch {
            continue
        }
    }

    if ($unsupported.Count -gt 0) {
        Add-Warning "OCR requires Python 3.10 through 3.13 because PaddlePaddle 3.3.1 has no Python 3.14 Windows wheel. Found unsupported interpreter(s): $($unsupported -join '; ')."
    }
    else {
        Add-Warning "Python 3.10 through 3.13 was not found. OCR is optional; install a compatible Python for Windows to enable PaddleOCR."
    }

    return $null
}

function Test-OcrPinnedPackageSet {
    param(
        [Parameter(Mandatory)]
        [string]$PythonPath,
        [object]$Manifest,
        [Parameter(Mandatory)]
        [object]$RuntimeSelection
    )

    if (-not $Manifest -or -not $Manifest.packages) {
        return
    }

    $verifyScript = Join-Path (Split-Path -Parent $PythonPath) "onlyrag_verify_ocr_runtime.py"
    $packages = @(
        $Manifest.packages |
            Where-Object { $_.name -ne "paddlepaddle" -and $_.name -ne "paddlepaddle-gpu" }
    )
    $packages += [pscustomobject]@{
        name = $RuntimeSelection.PaddlePackage
        version = $RuntimeSelection.PaddleVersion
        importName = "paddle"
    }
    if ($RuntimeSelection.CudnnPackage) {
        $packages += [pscustomobject]@{
            name = $RuntimeSelection.CudnnPackage
            version = $RuntimeSelection.CudnnVersion
            importName = $null
        }
    }

    $packagesJson = ($packages | ConvertTo-Json -Compress)
    $python = @"
import importlib
import importlib.metadata
import json
import sys

packages = json.loads(r'''$packagesJson''')
failures = []
for package in packages:
    name = package["name"]
    expected = package["version"]
    import_name = package.get("importName")
    try:
        actual = importlib.metadata.version(name)
    except importlib.metadata.PackageNotFoundError:
        failures.append(f"{name} missing")
        continue
    if actual != expected:
        failures.append(f"{name} expected {expected}, found {actual}")
    try:
        if import_name:
            importlib.import_module(import_name)
    except Exception as exc:
        failures.append(f"{import_name} import failed: {exc}")

if failures:
    print("; ".join(failures), file=sys.stderr)
    raise SystemExit(1)

print("OCR runtime packages verified.")
"@
    [System.IO.File]::WriteAllText($verifyScript, $python)
    try {
        Invoke-OcrNativeCaptured -FilePath $PythonPath -Arguments @($verifyScript) -Quiet | Out-Null
        Add-Verified "OCR $($RuntimeSelection.RuntimeName) package versions and imports match scripts\ocr\runtime-manifest.json."
        Invoke-Native -FilePath $PythonPath -Arguments @("-m", "pip", "check")
        Add-Verified "OCR Python dependency graph passed pip check."
        $bridgeOutput = Invoke-OcrNativeCaptured -FilePath $PythonPath -Arguments @($ocrBridgePath, "--mode", "check", "--device", $RuntimeSelection.Device) -Quiet
        $bridgeJson = @($bridgeOutput -split "(`r`n|`n|`r)" | Where-Object { $_.TrimStart().StartsWith("{") } | Select-Object -First 1)
        if ($bridgeJson.Count -eq 0) {
            throw "OCR bridge check did not return JSON."
        }

        $bridgeStatus = $bridgeJson[0] | ConvertFrom-Json
        if (-not $bridgeStatus.available) {
            throw "OCR bridge check failed: $($bridgeStatus.message)"
        }

        if ($RuntimeSelection.Device -eq "gpu" -and (-not $bridgeStatus.compiledWithCuda -or $bridgeStatus.cudaDeviceCount -lt 1)) {
            throw "OCR GPU bridge check did not report CUDA support."
        }

        $activeDevice = if ($bridgeStatus.activeDevice) { [string]$bridgeStatus.activeDevice } else { $RuntimeSelection.Device }
        Add-Verified "OCR bridge check completed with pinned $($RuntimeSelection.RuntimeName) runtime on $activeDevice."
    }
    finally {
        Remove-Item -LiteralPath $verifyScript -Force -ErrorAction SilentlyContinue
    }
}

function Ensure-OcrEnvironment {
    if ($SkipOcr) {
        Write-Result -Status "SKIP" -Message "OCR preparation skipped by -SkipOcr."
        Add-Manual "OCR remains manual. Default path: %LOCALAPPDATA%\OnlyRag\ocr-python\.venv\Scripts\python.exe."
        return
    }

    if (-not (Test-Path -LiteralPath $ocrBridgePath -PathType Leaf) -or -not (Test-Path -LiteralPath $ocrRequirementsPath -PathType Leaf)) {
        Add-Warning "OCR bridge or requirements were not found under scripts\ocr; OCR preparation skipped."
        return
    }

    $ocrManifest = Get-OcrRuntimeManifest
    Test-OcrDiskSpace -Manifest $ocrManifest
    $runtimeSelection = Get-OcrNvidiaRuntimeSelection -Manifest $ocrManifest
    if (-not (Test-Path -LiteralPath $runtimeSelection.RequirementsPath -PathType Leaf)) {
        Add-Warning "OCR requirements $($runtimeSelection.RequirementsFile) not found. Falling back to scripts\ocr\requirements.txt."
        $runtimeSelection = Get-OcrCpuRuntimeSelection -Manifest $ocrManifest -Detail "Runtime OCR CPU selezionato per fallback requirements."
    }
    Add-Verified "OCR runtime selected: $($runtimeSelection.RuntimeName). $($runtimeSelection.Detail)"

    $pythonCommand = Get-OcrPythonCommand
    if (-not $pythonCommand) {
        Add-Manual "Install Python 3.10, 3.11, 3.12, or 3.13 for Windows, then rerun bootstrap without -SkipOcr."
        Add-Manual "If OCR is already prepared elsewhere, set ONLYRAG_OCR_PYTHON to that environment's python.exe."
        return
    }

    Add-Verified "Python available for OCR: $($pythonCommand.VersionText) via $($pythonCommand.DisplayName)."

    $ocrInstallRoot = Join-Path $localDataRoot "ocr-python"
    $venvPath = Join-Path $ocrInstallRoot ".venv"
    $venvPython = Join-Path $venvPath "Scripts\python.exe"

    try {
        New-Item -ItemType Directory -Force -Path $ocrInstallRoot | Out-Null

        $venvNeedsCreate = $false
        if (-not (Test-Path -LiteralPath $venvPython -PathType Leaf)) {
            $venvNeedsCreate = $true
        }
        else {
            $venvVersionText = (& $venvPython --version 2>&1 | Out-String).Trim()
            $venvVersion = ConvertTo-VersionOrNull $venvVersionText
            if (-not $venvVersion -or -not (Test-OcrSupportedPythonVersion -Version $venvVersion)) {
                Add-Warning "OCR venv Python is not compatible with PaddlePaddle 3.3.1 ('$venvVersionText'). Recreating the venv with $($pythonCommand.VersionText)."
                Remove-Item -LiteralPath $venvPath -Recurse -Force
                $venvNeedsCreate = $true
            }
        }

        if ($venvNeedsCreate) {
            Invoke-Native -FilePath $pythonCommand.FilePath -Arguments (@($pythonCommand.Arguments) + @("-m", "venv", $venvPath))
            Add-Installed "OCR Python virtual environment created at $venvPath."
        }
        else {
            Add-Verified "OCR Python virtual environment already present at $venvPath."
        }

        Invoke-OcrNativeCaptured -FilePath $venvPython -Arguments @("-m", "pip", "install", "--upgrade", "pip", "--disable-pip-version-check") -Quiet | Out-Null

        # Upgrade OCR packages only when OCR requirement files changed; otherwise just install missing.
        $requirementsStamp = Join-Path $venvPath ".requirements-stamp"
        $requirementsChanged = $true
        $previousRuntimeName = $null
        $previousRequirementsFile = $null
        $requirementFiles = Get-ChildItem -LiteralPath (Split-Path -Parent $runtimeSelection.RequirementsPath) -Filter "requirements*.txt" -File
        if (Test-Path -LiteralPath $requirementsStamp) {
            $stampMtime = (Get-Item $requirementsStamp).LastWriteTimeUtc
            $reqMtime = ($requirementFiles | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).LastWriteTimeUtc
            $requirementsChanged = $reqMtime -gt $stampMtime
            try {
                $stamp = Get-Content -Raw -LiteralPath $requirementsStamp | ConvertFrom-Json
                $previousRuntimeName = $stamp.runtimeName
                $previousRequirementsFile = $stamp.requirementsFile
            }
            catch {
                $requirementsChanged = $true
            }
        }

        $runtimeChanged = $previousRuntimeName -ne $runtimeSelection.RuntimeName -or $previousRequirementsFile -ne $runtimeSelection.RequirementsFile
        if ($runtimeChanged) {
            Add-Verified "OCR runtime package set changed to $($runtimeSelection.RuntimeName); removing stale PaddlePaddle packages before install."
            Invoke-OcrNativeCaptured -FilePath $venvPython -Arguments @("-m", "pip", "uninstall", "-y", "paddlepaddle", "paddlepaddle-gpu") -Quiet | Out-Null
        }

        if ($requirementsChanged) {
            Write-Host "  $($runtimeSelection.RequirementsFile) changed - upgrading OCR $($runtimeSelection.RuntimeName) packages..." -ForegroundColor Cyan
            Invoke-OcrNativeCaptured -FilePath $venvPython -Arguments @("-m", "pip", "install", "--upgrade", "-r", $runtimeSelection.RequirementsPath, "--disable-pip-version-check") -Quiet | Out-Null
        }
        else {
            Write-Host "  $($runtimeSelection.RequirementsFile) unchanged - installing missing OCR $($runtimeSelection.RuntimeName) packages only..." -ForegroundColor DarkGray
            Invoke-OcrNativeCaptured -FilePath $venvPython -Arguments @("-m", "pip", "install", "-r", $runtimeSelection.RequirementsPath, "--disable-pip-version-check") -Quiet | Out-Null
        }
        $stampPayload = [pscustomobject]@{
            runtimeName = $runtimeSelection.RuntimeName
            requirementsFile = $runtimeSelection.RequirementsFile
            updatedAt = (Get-Date -Format 'o')
        } | ConvertTo-Json -Compress
        [System.IO.File]::WriteAllText($requirementsStamp, $stampPayload)
        Add-Installed "OCR Python packages prepared for $($runtimeSelection.RuntimeName) from scripts\ocr\$($runtimeSelection.RequirementsFile)."
        Test-OcrPinnedPackageSet -PythonPath $venvPython -Manifest $ocrManifest -RuntimeSelection $runtimeSelection
        Add-Manual "Set ONLYRAG_OCR_PYTHON=$venvPython only when running outside the default user profile."
        Add-Manual "PaddleOCR downloads models on first OCR use into the user profile cache; keep at least 5 GB free for packages and models."
    }
    catch {
        Add-Warning "OCR preparation failed: $($_.Exception.Message)"
        Add-Manual "OCR is optional. Rerun without -SkipOcr after fixing Python/pip/network access, or set ONLYRAG_OCR_PYTHON to a prepared environment."
    }
}
