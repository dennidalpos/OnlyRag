function Write-Result {
    param(
        [ValidateSet("OK", "WARN", "FAIL", "INFO", "SKIP")]
        [string]$Status,
        [string]$Message
    )

    $color = switch ($Status) {
        "OK" { "Green" }
        "WARN" { "Yellow" }
        "FAIL" { "Red" }
        "SKIP" { "DarkYellow" }
        default { "Cyan" }
    }

    Write-Host ("[{0}] {1}" -f $Status, $Message) -ForegroundColor $color
}

function Add-Verified {
    param([string]$Message)
    $script:Verified.Add($Message)
    Write-Result -Status "OK" -Message $Message
}

function Add-Installed {
    param([string]$Message)
    $script:Installed.Add($Message)
    Write-Result -Status "OK" -Message $Message
}

function Add-Warning {
    param([string]$Message)
    $script:Warnings.Add($Message)
    Write-Result -Status "WARN" -Message $Message
}

function Add-Failure {
    param([string]$Message)
    $script:Failures.Add($Message)
    Write-Result -Status "FAIL" -Message $Message
}

function Add-Manual {
    param([string]$Message)
    $script:Manual.Add($Message)
    Write-Result -Status "INFO" -Message $Message
}

function New-BootstrapPrerequisiteMessage {
    param(
        [Parameter(Mandatory)]
        [string]$Software,

        [Parameter(Mandatory)]
        [string]$MinimumVersion,

        [Parameter(Mandatory)]
        [string]$WhyRequired,

        [Parameter(Mandatory)]
        [string]$Instruction,

        [Parameter(Mandatory)]
        [string]$Verify
    )

    return "Software: $Software. Versione minima/supportata: $MinimumVersion. Perche serve: $WhyRequired. Istruzione: $Instruction. Verifica: $Verify"
}

function Invoke-Native {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,
        [Parameter()]
        [string[]]$Arguments = @(),
        [Parameter()]
        [string]$WorkingDirectory = $repoRoot
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

function ConvertTo-VersionOrNull {
    param([string]$Text)

    if ($Text -match 'v?(\d+)\.(\d+)\.(\d+)') {
        return [version]::new([int]$Matches[1], [int]$Matches[2], [int]$Matches[3])
    }

    return $null
}

function Test-NodeSupportedVersion {
    param([version]$Version)

    return (
        ($Version.Major -eq 20 -and $Version.Minor -ge 19) -or
        ($Version.Major -eq 22 -and $Version.Minor -ge 12) -or
        ($Version.Major -gt 22)
    )
}

function Get-WebView2Runtime {
    $edgeUpdateRoots = @(
        "HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients",
        "HKCU:\SOFTWARE\Microsoft\EdgeUpdate\Clients"
    )

    foreach ($root in $edgeUpdateRoots) {
        if (-not (Test-Path -LiteralPath $root)) {
            continue
        }

        foreach ($clientKey in Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue) {
            $props = Get-ItemProperty -LiteralPath $clientKey.PSPath -ErrorAction SilentlyContinue
            $name = $props.name
            if ($name -and $name -like "*WebView2*Runtime*") {
                return [pscustomobject]@{
                    Version = $props.pv
                    Source = $clientKey.PSPath
                }
            }
        }
    }

    $applicationRoots = @()
    foreach ($basePath in @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:LOCALAPPDATA)) {
        if ([string]::IsNullOrWhiteSpace($basePath)) {
            continue
        }

        $applicationRoots += Join-Path $basePath "Microsoft\EdgeWebView\Application"
    }

    foreach ($applicationRoot in $applicationRoots) {
        if (-not (Test-Path -LiteralPath $applicationRoot -PathType Container)) {
            continue
        }

        $runtimeExe = Get-ChildItem -LiteralPath $applicationRoot -Recurse -Filter "msedgewebview2.exe" -File -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($runtimeExe) {
            return [pscustomobject]@{
                Version = $runtimeExe.Directory.Name
                Source = $runtimeExe.FullName
            }
        }
    }

    return $null
}

function Get-LibreOfficeExecutable {
    param([string]$RequestedPath)

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $candidates += $RequestedPath
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ONLYRAG_LIBREOFFICE_PATH)) {
        $candidates += $env:ONLYRAG_LIBREOFFICE_PATH
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $candidates += Join-Path $env:ProgramFiles "LibreOffice\program\soffice.exe"
    }
    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
        $candidates += Join-Path ${env:ProgramFiles(x86)} "LibreOffice\program\soffice.exe"
    }

    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }

        if (Test-Path -LiteralPath $candidate -PathType Container) {
            $nestedCandidate = Join-Path $candidate "program\soffice.exe"
            if (Test-Path -LiteralPath $nestedCandidate -PathType Leaf) {
                return (Resolve-Path -LiteralPath $nestedCandidate).Path
            }
        }
    }

    return $null
}

function Ensure-OnlyRagDirectories {
    $directories = @(
        $localDataRoot,
        (Join-Path $localDataRoot "data"),
        (Join-Path $localDataRoot "documents"),
        (Join-Path $localDataRoot "documents\originals"),
        (Join-Path $localDataRoot "documents\renders"),
        (Join-Path $localDataRoot "documents\ocr-cache"),
        (Join-Path $localDataRoot "documents\exports"),
        (Join-Path $localDataRoot "logs"),
        (Join-Path $localDataRoot "temp")
    )

    foreach ($directory in $directories) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    Add-Installed "Local data directories ready under $localDataRoot."
}
