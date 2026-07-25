#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$IncludeInstaller,

    [switch]$ContinueOnError,

    [string]$NsisCompiler
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
$testOcrRuntimeManifestScript = Join-Path $PSScriptRoot "ocr\Test-OcrRuntimeManifest.ps1"
$gateDiagnosticsScript = Join-Path $PSScriptRoot "support\GateDiagnostics.ps1"
$buildSupportScript = Join-Path $PSScriptRoot "support\BuildSupport.ps1"
. $gateDiagnosticsScript
. $buildSupportScript

$script:GateResults = New-Object System.Collections.Generic.List[object]
$script:GateFailures = New-Object System.Collections.Generic.List[object]
$gateStopwatch = [System.Diagnostics.Stopwatch]::StartNew()

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
Write-Host "Continue on error: $(if ($ContinueOnError) { 'enabled' } else { 'disabled' })"
Write-Host "NSIS: $(if ([string]::IsNullOrWhiteSpace($NsisCompiler)) { 'auto-detect when installer is included' } else { $NsisCompiler })"

Invoke-GateStep "preflight" {
    if (-not $IsWindows) {
        throw (New-OnlyRagPrerequisiteMessage `
            -Software "Microsoft Windows" `
            -MinimumVersion "Windows 10 version 1809/build 17763 or Windows 11" `
            -WhyRequired "OnlyRag is a Windows WPF/WebView2 desktop app and the repository gate validates Windows packaging/runtime assumptions" `
            -Instruction "Run this gate on a supported Windows 10/11 client or Windows CI runner" `
            -Verify "Press Win+R, run winver, and confirm Windows 10 version 1809/build 17763 or newer, or Windows 11")
    }

    $dotnetCommand = Assert-OnlyRagDotNetSdk
    $nodeToolchain = Assert-OnlyRagNodeToolchain
    Write-Host "  dotnet: $($dotnetCommand.Source)"
    Write-Host "  node: $($nodeToolchain.Node.Source)"
    Write-Host "  npm: $($nodeToolchain.Npm.Source)"
    Write-GateToolVersion -Name "dotnet" -Arguments @("--version")
    Write-Host "  node version: $($nodeToolchain.NodeVersionText)"
    Write-GateToolVersion -Name "npm" -Arguments @("--version")

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
        if (-not (Test-Path -LiteralPath (Join-Path $webRoot "node_modules\.bin\tsc.cmd") -PathType Leaf)) {
            if (Test-Path -LiteralPath (Join-Path $webRoot "package-lock.json") -PathType Leaf) {
                $global:LASTEXITCODE = 0
                $prevEap = $ErrorActionPreference
                $ErrorActionPreference = "Continue"
                npm ci
                $ciCode = $LASTEXITCODE
                $ErrorActionPreference = $prevEap
                if ($ciCode -ne 0) {
                    Write-Host "  npm ci encountered a file lock issue; using npm install fallback..." -ForegroundColor Yellow
                    $global:LASTEXITCODE = 0
                    npm install
                }
            }
            else {
                npm install
            }
        }
        else {
            Write-Host "  web node_modules already present and valid; skipping restore."
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

Invoke-GateStep "OCR runtime manifest checks" {
    & $testOcrRuntimeManifestScript
}

Invoke-GateStep "web build" {
    & $buildWebScript -SkipInstallWhenUpToDate
}

Invoke-GateStep ".NET build" {
    & $buildAppScript -Configuration $Configuration -NoRestore -SkipWebBuild
}

if ($IncludeInstaller) {
    Invoke-GateStep "installer package" {
        $installerArguments = @{
            Configuration = $Configuration
        }
        if (-not [string]::IsNullOrWhiteSpace($NsisCompiler)) {
            $installerArguments.NsisCompiler = $NsisCompiler
        }

        & $buildInstallerScript @installerArguments
    }
}

Write-GateSummary
if ($script:GateFailures.Count -gt 0) {
    throw "Gate failed with $($script:GateFailures.Count) failing step(s)."
}

Write-Host ""
Write-Host "Gate completed successfully." -ForegroundColor Green
exit 0
