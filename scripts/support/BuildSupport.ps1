#requires -Version 7.0

. (Join-Path $PSScriptRoot "BuildPrerequisites.ps1")

function Invoke-OnlyRagNative {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,
        [Parameter()]
        [string[]]$Arguments = @(),
        [Parameter(Mandatory)]
        [string]$WorkingDirectory
    )

    Push-Location $WorkingDirectory
    try {
        & $FilePath @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "'$FilePath $($Arguments -join ' ')' failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

function Assert-OnlyRagPathUnderRepository {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory)]
        [string]$Path
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $repositoryPrefix = [System.IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($repositoryPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside the repository: $fullPath"
    }
}

function Test-OnlyRagNpmModulesUpToDate {
    param([Parameter(Mandatory)][string]$WebRoot)

    $lockFile = Join-Path $WebRoot "package-lock.json"
    $internalLock = Join-Path $WebRoot "node_modules\.package-lock.json"
    if (-not (Test-Path -LiteralPath $internalLock -PathType Leaf)) { return $false }
    if (-not (Test-Path -LiteralPath $lockFile -PathType Leaf)) { return $false }
    return (Get-Item -LiteralPath $internalLock).LastWriteTimeUtc -ge (Get-Item -LiteralPath $lockFile).LastWriteTimeUtc
}

function Invoke-OnlyRagWebBuild {
    param(
        [Parameter(Mandatory)]
        [string]$WebRoot,
        [switch]$SkipInstallWhenUpToDate
    )

    $nodeToolchain = Assert-OnlyRagNodeToolchain
    $npmCommand = $nodeToolchain.Npm

    $npmUpToDate = Test-OnlyRagNpmModulesUpToDate -WebRoot $WebRoot
    if ($SkipInstallWhenUpToDate -and $npmUpToDate) {
        Write-Host "  node_modules up to date - skipping npm install" -ForegroundColor DarkGray
    }
    elseif (Test-Path -LiteralPath (Join-Path $WebRoot "package-lock.json") -PathType Leaf) {
        Invoke-OnlyRagNative -FilePath $npmCommand.Source -Arguments @("ci") -WorkingDirectory $WebRoot
    }
    else {
        Invoke-OnlyRagNative -FilePath $npmCommand.Source -Arguments @("install") -WorkingDirectory $WebRoot
    }

    Invoke-OnlyRagNative -FilePath $npmCommand.Source -Arguments @("run", "build") -WorkingDirectory $WebRoot
}

function Get-OnlyRagInnoSetupCompiler {
    param([string]$RequestedPath)

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        if (-not (Test-Path -LiteralPath $RequestedPath -PathType Leaf)) {
            throw "Inno Setup compiler was not found at '$RequestedPath'."
        }

        return (Resolve-Path -LiteralPath $RequestedPath).Path
    }

    $command = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    return $null
}

function Get-OnlyRagSignTool {
    param([string]$RequestedPath)

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        if (-not (Test-Path -LiteralPath $RequestedPath -PathType Leaf)) {
            throw "signtool.exe was not found at '$RequestedPath'."
        }

        return (Resolve-Path -LiteralPath $RequestedPath).Path
    }

    $command = Get-Command "signtool.exe" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $roots = @()
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $roots += Join-Path $env:ProgramFiles "Windows Kits\10\bin"
    }
    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
        $roots += Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    }

    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) {
            continue
        }

        $candidate = Get-ChildItem -LiteralPath $root -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like "*\x64\signtool.exe" } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($candidate) {
            return $candidate.FullName
        }
    }

    return $null
}

function Invoke-OnlyRagInstallerSigning {
    param(
        [Parameter(Mandatory)]
        [string]$InstallerPath,
        [Parameter(Mandatory)]
        [string]$CertificateThumbprint,
        [Parameter(Mandatory)]
        [string]$TimestampServer,
        [string]$SignToolPath
    )

    if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) {
        throw "Installer to sign was not found: $InstallerPath"
    }
    if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        throw "A code-signing certificate thumbprint is required to sign the installer."
    }
    if ([string]::IsNullOrWhiteSpace($TimestampServer)) {
        throw "A timestamp server URL is required to sign the installer."
    }

    $resolvedSignTool = Get-OnlyRagSignTool -RequestedPath $SignToolPath
    if (-not $resolvedSignTool) {
        throw "signtool.exe was not found. Install the Windows SDK or pass -SignToolPath."
    }

    $normalizedThumbprint = $CertificateThumbprint.Replace(" ", "")
    Write-Host "Signing installer with certificate thumbprint $normalizedThumbprint..." -ForegroundColor Cyan
    Invoke-OnlyRagNative -FilePath $resolvedSignTool -WorkingDirectory (Split-Path -Parent $InstallerPath) -Arguments @(
        "sign",
        "/fd",
        "SHA256",
        "/td",
        "SHA256",
        "/tr",
        $TimestampServer,
        "/sha1",
        $normalizedThumbprint,
        $InstallerPath
    )

    Write-Host "Verifying installer signature..." -ForegroundColor Cyan
    Invoke-OnlyRagNative -FilePath $resolvedSignTool -WorkingDirectory (Split-Path -Parent $InstallerPath) -Arguments @(
        "verify",
        "/pa",
        "/v",
        $InstallerPath
    )
}

function Test-OnlyRagPublishPayload {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $requiredFiles = @(
        "OnlyRag.App.exe",
        "OnlyRag.App.dll",
        "OnlyRag.App.runtimeconfig.json",
        "coreclr.dll",
        "hostfxr.dll",
        "hostpolicy.dll",
        "PresentationNative_cor3.dll",
        "Microsoft.Web.WebView2.Core.dll",
        "Microsoft.Web.WebView2.Wpf.dll",
        "WebView2Loader.dll",
        "e_sqlite3.dll",
        "wwwroot\index.html",
        "qdrant\qdrant.exe",
        "qdrant\LICENSE",
        "scripts\ocr\install_ocr_runtime.ps1",
        "scripts\ocr\paddle_ocr_bridge.py",
        "scripts\ocr\requirements.txt",
        "scripts\ocr\requirements-common.txt",
        "scripts\ocr\requirements-cpu.txt",
        "scripts\ocr\requirements-nvidia-cu118.txt",
        "scripts\ocr\requirements-nvidia-cu126.txt",
        "scripts\ocr\requirements-nvidia-cu129.txt",
        "scripts\ocr\requirements-nvidia-cu130.txt",
        "scripts\ocr\runtime-manifest.json"
    )

    foreach ($requiredFile in $requiredFiles) {
        $fullPath = Join-Path $Path $requiredFile
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw "Publish output is incomplete. Missing '$requiredFile'."
        }
    }

    $forbiddenMatches = Get-ChildItem -LiteralPath $Path -Recurse -Force | Where-Object {
        $relativePath = [System.IO.Path]::GetRelativePath($Path, $_.FullName)
        $relativePath -like "OnlyRag.App.exe.WebView2*" -or
        $relativePath -like "node_modules*" -or
        $relativePath -like "documents*" -or
        $relativePath -like "data*" -or
        $relativePath -like "logs*" -or
        $relativePath -like "temp*" -or
        $_.Name -like "*.db" -or
        $_.Name -like "*.sqlite" -or
        $_.Name -like "*.sqlite3" -or
        $_.FullName.IndexOf('\ollama\models\', [System.StringComparison]::OrdinalIgnoreCase) -ge 0
    }

    if ($forbiddenMatches) {
        $items = ($forbiddenMatches | Select-Object -First 20 -ExpandProperty FullName) -join [Environment]::NewLine
        throw "Publish output contains files or directories that must not be packaged:$([Environment]::NewLine)$items"
    }
}
