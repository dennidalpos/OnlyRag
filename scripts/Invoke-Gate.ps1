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

function Test-DotNetPackageVulnerabilities {
    param(
        [Parameter(Mandatory)]
        [string]$SolutionPath
    )

    $global:LASTEXITCODE = 0
    $output = dotnet list $SolutionPath package --vulnerable --include-transitive --format json 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        Write-Host $output
        throw "dotnet package vulnerability audit failed with exit code $LASTEXITCODE."
    }

    if ([string]::IsNullOrWhiteSpace($output)) {
        Write-Host "No vulnerable NuGet packages reported."
        return
    }

    $report = $output | ConvertFrom-Json
    $findings = New-Object System.Collections.Generic.List[string]

    foreach ($project in (@(Get-JsonPropertyValue -Object $report -Name "projects") | Where-Object { $null -ne $_ })) {
        foreach ($framework in (@(Get-JsonPropertyValue -Object $project -Name "frameworks") | Where-Object { $null -ne $_ })) {
            foreach ($packageSetName in @("topLevelPackages", "transitivePackages")) {
                foreach ($package in (@(Get-JsonPropertyValue -Object $framework -Name $packageSetName) | Where-Object { $null -ne $_ })) {
                    $vulnerabilities = @(Get-JsonPropertyValue -Object $package -Name "vulnerabilities") | Where-Object { $null -ne $_ }
                    if ($vulnerabilities.Count -eq 0) {
                        continue
                    }

                    $packageId = Get-JsonPropertyValue -Object $package -Name "id"
                    $resolvedVersion = Get-JsonPropertyValue -Object $package -Name "resolvedVersion"
                    foreach ($vulnerability in $vulnerabilities) {
                        $severity = Get-JsonPropertyValue -Object $vulnerability -Name "severity"
                        $advisoryUrl = Get-JsonPropertyValue -Object $vulnerability -Name "advisoryUrl"
                        $findings.Add("$packageId $resolvedVersion [$severity] $advisoryUrl")
                    }
                }
            }
        }
    }

    if ($findings.Count -gt 0) {
        Write-Host "Vulnerable NuGet packages were found:" -ForegroundColor Red
        foreach ($finding in $findings) {
            Write-Host "  $finding"
        }

        throw "NuGet package vulnerability audit failed."
    }

    Write-Host "No vulnerable NuGet packages reported."
}

Write-Host "OnlyRag repository gate" -ForegroundColor Cyan
Write-Host "Repository: $repoRoot"
Write-Host "Configuration: $Configuration"
Write-Host "Installer: $(if ($IncludeInstaller) { 'included' } else { 'skipped by default' })"
Write-Host "Inno Setup: $(if ([string]::IsNullOrWhiteSpace($InnoSetupCompiler)) { 'auto-detect when installer is included' } else { $InnoSetupCompiler })"

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

Invoke-GateStep "npm production dependency audit" {
    Push-Location $webRoot
    try {
        npm audit --omit=dev --audit-level=moderate
    }
    finally {
        Pop-Location
    }
}

Invoke-GateStep "NuGet dependency vulnerability audit" {
    Test-DotNetPackageVulnerabilities -SolutionPath $solution
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

Invoke-GateStep "web lint" {
    Push-Location $webRoot
    try {
        npm run lint
    }
    finally {
        Pop-Location
    }
}

Invoke-GateStep "web format check" {
    Push-Location $webRoot
    try {
        npm run format:check
    }
    finally {
        Pop-Location
    }
}

Invoke-GateStep "web tests" {
    Push-Location $webRoot
    try {
        npm run test
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
    & $buildWebScript -SkipInstallWhenUpToDate
}

Invoke-GateStep ".NET build" {
    & $buildAppScript -Configuration $Configuration -NoRestore
}

if ($IncludeInstaller) {
    Invoke-GateStep "installer package" {
        $installerArguments = @{
            Configuration = $Configuration
        }
        if (-not [string]::IsNullOrWhiteSpace($InnoSetupCompiler)) {
            $installerArguments.InnoSetupCompiler = $InnoSetupCompiler
        }

        & $buildInstallerScript @installerArguments
    }
}

Write-Host ""
Write-Host "Gate completed successfully." -ForegroundColor Green
exit 0
