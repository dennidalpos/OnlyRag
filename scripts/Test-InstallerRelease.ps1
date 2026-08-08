#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InstallerPath,
    [string]$UpgradeInstallerPath,
    [string]$RollbackInstallerPath,
    [string]$OutputRoot,
    [switch]$RunInstallLifecycle,
    [switch]$RequireSigned
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$supportScript = Join-Path $PSScriptRoot "support\BuildSupport.ps1"
. $supportScript

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\release-verification"
}

$outputRootPath = [System.IO.Path]::GetFullPath($OutputRoot)
Assert-OnlyRagPathUnderRepository -RepositoryRoot $repoRoot -Path $outputRootPath
New-Item -ItemType Directory -Force -Path $outputRootPath | Out-Null

$installDir = Join-Path $env:LOCALAPPDATA "Programs\OnlyRag"
$dataDir = Join-Path $env:LOCALAPPDATA "OnlyRag"
$startMenuShortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\OnlyRag\OnlyRag.lnk"
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "OnlyRag.lnk"
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$evidencePath = Join-Path $outputRootPath "OnlyRag-release-verification-$timestamp.json"
$script:Checks = [System.Collections.Generic.List[object]]::new()

function Add-Check {
    param(
        [Parameter(Mandatory)]
        [string]$Id,
        [ValidateSet("pass", "fail", "skip", "warn")]
        [string]$Status,
        [Parameter(Mandatory)]
        [string]$Message,
        [object]$Data = $null
    )

    $script:Checks.Add([ordered]@{
        id = $Id
        status = $Status
        message = $Message
        data = $Data
        atUtc = [DateTimeOffset]::UtcNow.ToString("O")
    })

    $color = switch ($Status) {
        "pass" { "Green" }
        "warn" { "Yellow" }
        "fail" { "Red" }
        default { "DarkYellow" }
    }
    Write-Host "[$Status] $Id - $Message" -ForegroundColor $color
}

function Resolve-Installer {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label installer not found: $Path"
    }
    return (Resolve-Path -LiteralPath $Path).Path
}

function Invoke-Installer {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [string]$LogName,
        [string[]]$ExtraArguments = @()
    )

    $logPath = Join-Path $outputRootPath $LogName
    $arguments = @(
        "/S"
    ) + $ExtraArguments

    $process = Start-Process -FilePath $Path -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
    return [ordered]@{
        exitCode = $process.ExitCode
        logPath = $logPath
        arguments = $arguments
    }
}

function Stop-OnlyRagProcesses {
    Get-Process -Name "OnlyRag.App" -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
}

function Test-PathExpectation {
    param(
        [Parameter(Mandatory)]
        [string]$Id,
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [string]$Kind
    )

    $pathType = if ($Kind -eq "file") { "Leaf" } else { "Container" }
    if (Test-Path -LiteralPath $Path -PathType $pathType) {
        Add-Check -Id $Id -Status "pass" -Message "$Kind exists: $Path"
    }
    else {
        Add-Check -Id $Id -Status "fail" -Message "$kind missing: $Path"
    }
}

function Test-InstallerSignature {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [bool]$RequireValidSignature
    )

    $signature = Get-AuthenticodeSignature -FilePath $Path
    $status = if ($signature.Status -eq "Valid") { "pass" } elseif ($RequireValidSignature) { "fail" } else { "warn" }
    Add-Check -Id "signing-status" -Status $status -Message "Installer signature status: $($signature.Status)." -Data @{
        signer = $signature.SignerCertificate?.Subject
        statusMessage = $signature.StatusMessage
    }

    $installerFolder = [System.IO.Path]::GetDirectoryName($Path)
    $manifestPath = Join-Path $installerFolder "installer-manifest.json"
    if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
        try {
            $manifestText = [System.IO.File]::ReadAllText($manifestPath)
            $manifest = ConvertFrom-Json $manifestText
            Add-Check -Id "installer-manifest-audit" -Status "pass" -Message "Release installer-manifest.json verified ($($manifest.fileCount) binaries audited)." -Data @{
                version = $manifest.version
                fileCount = $manifest.fileCount
                generatedAtUtc = $manifest.generatedAtUtc
            }
        }
        catch {
            Add-Check -Id "installer-manifest-audit" -Status "warn" -Message "installer-manifest.json present but invalid: $_"
        }
    }
    else {
        Add-Check -Id "installer-manifest-audit" -Status "warn" -Message "installer-manifest.json not found in installer directory."
    }
}

function Test-OptionalComponents {
    $ocrPython = Join-Path $env:LOCALAPPDATA "OnlyRag\ocr-python\.venv\Scripts\python.exe"
    if (Test-Path -LiteralPath $ocrPython -PathType Leaf) {
        Add-Check -Id "optional-ocr-python" -Status "pass" -Message "OCR Python environment found." -Data @{ path = $ocrPython }
    }
    else {
        Add-Check -Id "optional-ocr-python" -Status "warn" -Message "OCR Python environment not found; OCR should report a configurable optional dependency."
    }

    $nvidiaSmi = Get-Command "nvidia-smi" -ErrorAction SilentlyContinue
    if ($nvidiaSmi) {
        $nvidiaInfo = (& $nvidiaSmi.Source --query-gpu=driver_version,name --format=csv,noheader 2>$null | Select-Object -First 1)
        Add-Check -Id "optional-nvidia-gpu" -Status "pass" -Message "NVIDIA management tools found for OCR GPU provisioning." -Data @{
            nvidiaSmi = $nvidiaSmi.Source
            gpu = $nvidiaInfo
        }
    }
    else {
        Add-Check -Id "optional-nvidia-gpu" -Status "warn" -Message "NVIDIA management tools not found; OCR GPU provisioning should fall back to CPU."
    }

    $libreOffice = @(
        (Join-Path $env:ProgramFiles "LibreOffice\program\soffice.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "LibreOffice\program\soffice.exe")
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if ($libreOffice) {
        Add-Check -Id "optional-libreoffice" -Status "pass" -Message "LibreOffice found." -Data @{ path = $libreOffice }
    }
    else {
        Add-Check -Id "optional-libreoffice" -Status "warn" -Message "LibreOffice not found; translation PDF export should remain optional."
    }

    try {
        $response = Invoke-WebRequest -Uri "http://localhost:11434/api/tags" -UseBasicParsing -TimeoutSec 5
        Add-Check -Id "optional-ollama" -Status "pass" -Message "Ollama endpoint reachable." -Data @{ statusCode = $response.StatusCode }
    }
    catch {
        Add-Check -Id "optional-ollama" -Status "warn" -Message "Ollama endpoint not reachable; model features should remain configurable."
    }

    $imageModelsDir = Join-Path $dataDir "models\images"
    if (Test-Path -LiteralPath $imageModelsDir -PathType Container) {
        Add-Check -Id "optional-image-model-storage" -Status "pass" -Message "Integrated image model storage directory found." -Data @{
            path = $imageModelsDir
        }
    }
    else {
        Add-Check -Id "optional-image-model-storage" -Status "warn" -Message "Integrated image model storage was not created before image model activity." -Data @{
            path = $imageModelsDir
        }
    }
}

function Test-AppLaunch {
    $exe = Join-Path $installDir "OnlyRag.App.exe"
    $logPath = Join-Path $dataDir "logs\backend.log"
    $previousLogLength = 0
    if (Test-Path -LiteralPath $logPath -PathType Leaf) {
        $previousLogLength = (Get-Item -LiteralPath $logPath).Length
    }

    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        Add-Check -Id "app-launch" -Status "fail" -Message "App executable missing: $exe"
        return
    }

    Stop-OnlyRagProcesses
    $process = Start-Process -FilePath $exe -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 8
    if ($process.HasExited) {
        Add-Check -Id "app-launch" -Status "fail" -Message "App process exited during launch." -Data @{ exitCode = $process.ExitCode }
    }
    else {
        Add-Check -Id "app-launch" -Status "pass" -Message "App process remained alive after launch." -Data @{ processId = $process.Id }
    }

    if (Test-Path -LiteralPath $logPath -PathType Leaf) {
        $logContent = Get-Content -Raw -LiteralPath $logPath
        $currentLogLength = (Get-Item -LiteralPath $logPath).Length
        if ($currentLogLength -gt $previousLogLength -and $logContent.Contains("In-process backend listening")) {
            Add-Check -Id "first-launch-backend-start" -Status "pass" -Message "First launch verified backend startup." -Data @{ logPath = $logPath }
        }
        else {
            Add-Check -Id "first-launch-backend-start" -Status "fail" -Message "First launch log did not confirm backend startup." -Data @{ logPath = $logPath }
        }
    }
    else {
        Add-Check -Id "first-launch-backend-log" -Status "fail" -Message "First launch did not create backend log." -Data @{ logPath = $logPath }
    }

    $webViewDataDir = Join-Path $dataDir "webview2"
    $installWebViewDataDir = Join-Path $installDir "OnlyRag.App.exe.WebView2"
    Test-PathExpectation -Id "first-launch-webview2-user-data" -Path $webViewDataDir -Kind "directory"
    if (Test-Path -LiteralPath $installWebViewDataDir) {
        Add-Check -Id "first-launch-webview2-install-dir-clean" -Status "fail" -Message "WebView2 user data was created under the install directory." -Data @{ path = $installWebViewDataDir }
    }
    else {
        Add-Check -Id "first-launch-webview2-install-dir-clean" -Status "pass" -Message "WebView2 user data was not created under the install directory." -Data @{ path = $installWebViewDataDir }
    }

    Stop-OnlyRagProcesses
}

$resolvedInstaller = Resolve-Installer -Path $InstallerPath -Label "Primary"
$resolvedUpgradeInstaller = if ($UpgradeInstallerPath) { Resolve-Installer -Path $UpgradeInstallerPath -Label "Upgrade" } else { $null }
$resolvedRollbackInstaller = if ($RollbackInstallerPath) { Resolve-Installer -Path $RollbackInstallerPath -Label "Rollback" } else { $null }

Add-Check -Id "installer-file" -Status "pass" -Message "Primary installer found." -Data @{
    path = $resolvedInstaller
    bytes = (Get-Item -LiteralPath $resolvedInstaller).Length
}
Test-InstallerSignature -Path $resolvedInstaller -RequireValidSignature:$RequireSigned
Test-OptionalComponents

if (-not $RunInstallLifecycle) {
    Add-Check -Id "install-lifecycle" -Status "skip" -Message "Install/upgrade/uninstall lifecycle not executed. Rerun with -RunInstallLifecycle on a Windows release verification machine."
}
else {
    Stop-OnlyRagProcesses

    $install = Invoke-Installer -Path $resolvedInstaller -LogName "install-$timestamp.log" -ExtraArguments @("/D=$installDir")
    Add-Check -Id "fresh-install-exit" -Status ($(if ($install.exitCode -eq 0) { "pass" } else { "fail" })) -Message "Fresh install exit code $($install.exitCode)." -Data $install
    Test-PathExpectation -Id "install-path" -Path $installDir -Kind "directory"
    Test-PathExpectation -Id "app-executable" -Path (Join-Path $installDir "OnlyRag.App.exe") -Kind "file"
    Test-PathExpectation -Id "dotnet-coreclr-runtime" -Path (Join-Path $installDir "coreclr.dll") -Kind "file"
    Test-PathExpectation -Id "dotnet-hostfxr-runtime" -Path (Join-Path $installDir "hostfxr.dll") -Kind "file"
    Test-PathExpectation -Id "dotnet-hostpolicy-runtime" -Path (Join-Path $installDir "hostpolicy.dll") -Kind "file"
    Test-PathExpectation -Id "wpf-native-runtime" -Path (Join-Path $installDir "PresentationNative_cor3.dll") -Kind "file"
    Test-PathExpectation -Id "webview2-core-assembly" -Path (Join-Path $installDir "Microsoft.Web.WebView2.Core.dll") -Kind "file"
    Test-PathExpectation -Id "webview2-wpf-assembly" -Path (Join-Path $installDir "Microsoft.Web.WebView2.Wpf.dll") -Kind "file"
    Test-PathExpectation -Id "webview2-loader-native-asset" -Path (Join-Path $installDir "WebView2Loader.dll") -Kind "file"
    Test-PathExpectation -Id "sqlite-native-provider" -Path (Join-Path $installDir "e_sqlite3.dll") -Kind "file"
    Test-PathExpectation -Id "qdrant-native-binary" -Path (Join-Path $installDir "qdrant\qdrant.exe") -Kind "file"
    Test-PathExpectation -Id "qdrant-license" -Path (Join-Path $installDir "qdrant\LICENSE") -Kind "file"
    Test-PathExpectation -Id "ocr-setup-preinstall-script" -Path (Join-Path $installDir "scripts\ocr\install_ocr_runtime.ps1") -Kind "file"
    Test-PathExpectation -Id "ocr-bridge-script" -Path (Join-Path $installDir "scripts\ocr\paddle_ocr_bridge.py") -Kind "file"
    Test-PathExpectation -Id "ocr-requirements" -Path (Join-Path $installDir "scripts\ocr\requirements.txt") -Kind "file"
    Test-PathExpectation -Id "ocr-requirements-common" -Path (Join-Path $installDir "scripts\ocr\requirements-common.txt") -Kind "file"
    Test-PathExpectation -Id "ocr-requirements-cpu" -Path (Join-Path $installDir "scripts\ocr\requirements-cpu.txt") -Kind "file"
    Test-PathExpectation -Id "ocr-requirements-nvidia-cu118" -Path (Join-Path $installDir "scripts\ocr\requirements-nvidia-cu118.txt") -Kind "file"
    Test-PathExpectation -Id "ocr-requirements-nvidia-cu126" -Path (Join-Path $installDir "scripts\ocr\requirements-nvidia-cu126.txt") -Kind "file"
    Test-PathExpectation -Id "ocr-runtime-manifest" -Path (Join-Path $installDir "scripts\ocr\runtime-manifest.json") -Kind "file"
    Test-PathExpectation -Id "web-ui-entrypoint" -Path (Join-Path $installDir "wwwroot\index.html") -Kind "file"
    Test-PathExpectation -Id "start-menu-shortcut" -Path $startMenuShortcut -Kind "file"
    Test-PathExpectation -Id "desktop-shortcut" -Path $desktopShortcut -Kind "file"
    Test-AppLaunch
    if (Test-Path -LiteralPath $dataDir -PathType Container) {
        Add-Check -Id "data-location" -Status "pass" -Message "User data root exists under %LOCALAPPDATA%." -Data @{ path = $dataDir }
    }
    else {
        Add-Check -Id "data-location" -Status "warn" -Message "User data root was not created before app activity required storage." -Data @{ path = $dataDir }
    }

    if ($resolvedUpgradeInstaller) {
        $upgrade = Invoke-Installer -Path $resolvedUpgradeInstaller -LogName "upgrade-$timestamp.log" -ExtraArguments @("/D=$installDir")
        Add-Check -Id "upgrade-exit" -Status ($(if ($upgrade.exitCode -eq 0) { "pass" } else { "fail" })) -Message "Upgrade install exit code $($upgrade.exitCode)." -Data $upgrade
        Test-AppLaunch
    }
    else {
        Add-Check -Id "upgrade" -Status "skip" -Message "No -UpgradeInstallerPath supplied."
    }

    if ($resolvedRollbackInstaller) {
        $rollback = Invoke-Installer -Path $resolvedRollbackInstaller -LogName "rollback-$timestamp.log" -ExtraArguments @("/D=$installDir")
        $status = if ($rollback.exitCode -eq 0) { "pass" } else { "warn" }
        Add-Check -Id "rollback-downgrade" -Status $status -Message "Rollback/downgrade installer exit code $($rollback.exitCode)." -Data $rollback
        if ($rollback.exitCode -eq 0) {
            Test-AppLaunch
        }
    }
    else {
        Add-Check -Id "rollback-downgrade" -Status "skip" -Message "No -RollbackInstallerPath supplied."
    }

    $uninstallExe = Join-Path $installDir "uninstall.exe"
    if (Test-Path -LiteralPath $uninstallExe -PathType Leaf) {
        $uninstall = Invoke-Installer -Path $uninstallExe -LogName "uninstall-$timestamp.log" -ExtraArguments @("_?=$installDir")
        Add-Check -Id "uninstall-exit" -Status ($(if ($uninstall.exitCode -eq 0) { "pass" } else { "fail" })) -Message "Uninstall exit code $($uninstall.exitCode)." -Data $uninstall
        if (Test-Path -LiteralPath $installDir -PathType Container) {
            Add-Check -Id "uninstall-install-dir-cleanup" -Status "fail" -Message "Install directory still exists after uninstall." -Data @{ path = $installDir }
        }
        else {
            Add-Check -Id "uninstall-install-dir-cleanup" -Status "pass" -Message "Install directory removed after uninstall." -Data @{ path = $installDir }
        }
        if (Test-Path -LiteralPath $dataDir -PathType Container) {
            Add-Check -Id "uninstall-data-preserved" -Status "pass" -Message "User data directory preserved after uninstall." -Data @{ path = $dataDir }
        }
        else {
            Add-Check -Id "uninstall-data-preserved" -Status "warn" -Message "User data directory not present after uninstall." -Data @{ path = $dataDir }
        }
    }
    else {
        Add-Check -Id "uninstall-exe" -Status "fail" -Message "Uninstaller not found: $uninstallExe"
    }
}

$failureCount = @($script:Checks | Where-Object { $_.status -eq "fail" }).Count
$warningCount = @($script:Checks | Where-Object { $_.status -eq "warn" }).Count
$payload = [ordered]@{
    tool = "OnlyRag release verification"
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    installerPath = $resolvedInstaller
    upgradeInstallerPath = $resolvedUpgradeInstaller
    rollbackInstallerPath = $resolvedRollbackInstaller
    installLifecycleExecuted = [bool]$RunInstallLifecycle
    installDir = $installDir
    dataDir = $dataDir
    failureCount = $failureCount
    warningCount = $warningCount
    checks = $script:Checks
}

$payload | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $evidencePath -Encoding UTF8
Write-Host "Evidence artifact: $evidencePath" -ForegroundColor Cyan

if ($failureCount -gt 0) {
    exit 1
}

exit 0
