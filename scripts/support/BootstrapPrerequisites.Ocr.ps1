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

function Test-OcrPinnedPackageSet {
    param(
        [Parameter(Mandatory)]
        [string]$PythonPath,
        [object]$Manifest
    )

    if (-not $Manifest -or -not $Manifest.packages) {
        return
    }

    $verifyScript = Join-Path (Split-Path -Parent $PythonPath) "onlyrag_verify_ocr_runtime.py"
    $packagesJson = ($Manifest.packages | ConvertTo-Json -Compress)
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
    import_name = package["importName"]
    try:
        actual = importlib.metadata.version(name)
    except importlib.metadata.PackageNotFoundError:
        failures.append(f"{name} missing")
        continue
    if actual != expected:
        failures.append(f"{name} expected {expected}, found {actual}")
    try:
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
        Invoke-Native -FilePath $PythonPath -Arguments @($verifyScript)
        Add-Verified "OCR package versions and imports match scripts\ocr\runtime-manifest.json."
        Invoke-Native -FilePath $PythonPath -Arguments @("-m", "pip", "check")
        Add-Verified "OCR Python dependency graph passed pip check."
        Invoke-Native -FilePath $PythonPath -Arguments @($ocrBridgePath, "--mode", "check")
        Add-Verified "OCR bridge check completed with pinned runtime."
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

    $pythonCommand = Get-Command python -ErrorAction SilentlyContinue
    if (-not $pythonCommand) {
        Add-Warning "Python was not found. OCR is optional; install Python 3.10+ for Windows to enable PaddleOCR."
        Add-Manual "After installing Python, rerun pwsh .\scripts\Bootstrap-Prerequisites.ps1 or set ONLYRAG_OCR_PYTHON manually."
        return
    }

    $pythonVersionText = (& $pythonCommand.Source --version 2>&1 | Out-String).Trim()
    $pythonVersion = ConvertTo-VersionOrNull $pythonVersionText
    if (-not $pythonVersion -or $pythonVersion.Major -lt 3 -or ($pythonVersion.Major -eq 3 -and $pythonVersion.Minor -lt 10)) {
        Add-Warning "OCR requires Python 3.10+; found '$pythonVersionText'. OCR preparation skipped."
        Add-Manual "Install Python 3.10+ for Windows, then rerun bootstrap without -SkipOcr."
        return
    }

    Add-Verified "Python available for OCR: $pythonVersionText."

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
            if (-not $venvVersion) {
                Add-Warning "OCR venv Python non funzionante (creato con una versione diversa di Python). Ricreo il venv..."
                Remove-Item -Recurse -Force $venvPath
                $venvNeedsCreate = $true
            }
        }

        if ($venvNeedsCreate) {
            Invoke-Native -FilePath $pythonCommand.Source -Arguments @("-m", "venv", $venvPath)
            Add-Installed "OCR Python virtual environment created at $venvPath."
        }
        else {
            Add-Verified "OCR Python virtual environment already present at $venvPath."
        }

        Invoke-Native -FilePath $venvPython -Arguments @("-m", "pip", "install", "--upgrade", "pip", "--disable-pip-version-check")

        # Upgrade OCR packages only when requirements.txt has changed; otherwise just install missing.
        $requirementsStamp = Join-Path $venvPath ".requirements-stamp"
        $requirementsChanged = $true
        if (Test-Path -LiteralPath $requirementsStamp) {
            $stampMtime = (Get-Item $requirementsStamp).LastWriteTimeUtc
            $reqMtime   = (Get-Item $ocrRequirementsPath).LastWriteTimeUtc
            $requirementsChanged = $reqMtime -gt $stampMtime
        }

        if ($requirementsChanged) {
            Write-Host "  requirements.txt changed — upgrading OCR packages..." -ForegroundColor Cyan
            Invoke-Native -FilePath $venvPython -Arguments @("-m", "pip", "install", "--upgrade", "-r", $ocrRequirementsPath, "--disable-pip-version-check")
        }
        else {
            Write-Host "  requirements.txt unchanged — installing missing OCR packages only..." -ForegroundColor DarkGray
            Invoke-Native -FilePath $venvPython -Arguments @("-m", "pip", "install", "-r", $ocrRequirementsPath, "--disable-pip-version-check")
        }
        (Get-Item $ocrRequirementsPath).LastWriteTimeUtc | Out-Null
        [System.IO.File]::WriteAllText($requirementsStamp, (Get-Date -Format 'o'))
        Add-Installed "OCR Python packages prepared from scripts\ocr\requirements.txt."
        Test-OcrPinnedPackageSet -PythonPath $venvPython -Manifest $ocrManifest
        Add-Manual "Set ONLYRAG_OCR_PYTHON=$venvPython only when running outside the default user profile."
        Add-Manual "PaddleOCR downloads models on first OCR use into the user profile cache; keep at least 5 GB free for packages and models."
    }
    catch {
        Add-Warning "OCR preparation failed: $($_.Exception.Message)"
        Add-Manual "OCR is optional. Rerun without -SkipOcr after fixing Python/pip/network access, or set ONLYRAG_OCR_PYTHON to a prepared environment."
    }
}
