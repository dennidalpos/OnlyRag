#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$IncludeInstaller,

    [string]$InnoSetupCompiler
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$solution = Join-Path $repoRoot "OnlyRag.sln"
$webRoot = Join-Path $repoRoot "src\OnlyRag.Web"
$buildWebScript = Join-Path $PSScriptRoot "Build-Web.ps1"
$buildAppScript = Join-Path $PSScriptRoot "Build-App.ps1"
$buildInstallerScript = Join-Path $PSScriptRoot "Build-Installer.ps1"
$testInstallerPrerequisitesScript = Join-Path $PSScriptRoot "Test-InstallerPrerequisites.ps1"

function Invoke-GateStep {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [scriptblock]$Action
    )

    Write-Host ""
    Write-Host "==> $Name" -ForegroundColor Cyan

    $global:LASTEXITCODE = 0
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
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

Write-Host "OnlyRag repository gate" -ForegroundColor Cyan
Write-Host "Repository: $repoRoot"
Write-Host "Configuration: $Configuration"
Write-Host "Installer: $(if ($IncludeInstaller) { 'included' } else { 'skipped by default' })"

Invoke-GateStep "preflight" {
    if (-not $IsWindows) {
        throw "OnlyRag targets Windows; run this gate on Windows."
    }

    Assert-CommandAvailable -Name "dotnet" -InstallHint "Install .NET 10 SDK for Windows."
    Assert-CommandAvailable -Name "npm" -InstallHint "Install Node.js with npm matching src\OnlyRag.Web\package.json."

    if (-not (Test-Path -LiteralPath $solution -PathType Leaf)) {
        throw "Solution not found: $solution"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $webRoot "package.json") -PathType Leaf)) {
        throw "Web package manifest not found under $webRoot."
    }
}

Invoke-GateStep "restore web dependencies" {
    Push-Location $webRoot
    try {
        if (Test-Path -LiteralPath (Join-Path $webRoot "package-lock.json") -PathType Leaf) {
            npm ci
        }
        else {
            npm install
        }
    }
    finally {
        Pop-Location
    }
}

Invoke-GateStep "restore .NET packages" {
    dotnet restore $solution
}

Invoke-GateStep "web typecheck" {
    Push-Location $webRoot
    try {
        npm run typecheck
    }
    finally {
        Pop-Location
    }
}

Invoke-GateStep ".NET tests" {
    dotnet test $solution --configuration $Configuration --no-restore --logger "console;verbosity=minimal"
}

Invoke-GateStep "installer prerequisite checks" {
    & $testInstallerPrerequisitesScript -SelfTest
}

Invoke-GateStep "web build" {
    & $buildWebScript
}

Invoke-GateStep ".NET build" {
    & $buildAppScript -Configuration $Configuration
}

if ($IncludeInstaller) {
    Invoke-GateStep "installer package" {
        $arguments = @("-Configuration", $Configuration)
        if (-not [string]::IsNullOrWhiteSpace($InnoSetupCompiler)) {
            $arguments += @("-InnoSetupCompiler", $InnoSetupCompiler)
        }

        & $buildInstallerScript @arguments
    }
}

Write-Host ""
Write-Host "Gate completed successfully." -ForegroundColor Green
exit 0
