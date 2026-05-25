#requires -Version 7.0
[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$PreserveArtifacts,

    [switch]$PreserveDependencies,

    [switch]$IncludeArtifacts,

    [switch]$IncludeDependencies
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$supportScript = Join-Path $PSScriptRoot "support\BuildSupport.ps1"
. $supportScript

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$cleanArtifacts = -not $PreserveArtifacts
$cleanDependencies = -not $PreserveDependencies

if ($IncludeArtifacts -and $PreserveArtifacts) {
    throw "Use either -IncludeArtifacts or -PreserveArtifacts, not both."
}

if ($IncludeDependencies -and $PreserveDependencies) {
    throw "Use either -IncludeDependencies or -PreserveDependencies, not both."
}

if ($IncludeArtifacts) {
    $cleanArtifacts = $true
}

if ($IncludeDependencies) {
    $cleanDependencies = $true
}

function Resolve-OnlyRagRepositoryPath {
    param(
        [Parameter(Mandatory)]
        [string]$RelativePath
    )

    $candidate = Join-Path $repoRoot $RelativePath
    $fullPath = [System.IO.Path]::GetFullPath($candidate)
    Assert-OnlyRagPathUnderRepository -RepositoryRoot $repoRoot -Path $fullPath
    return $fullPath
}

function Remove-OnlyRagPathIfExists {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    Assert-OnlyRagPathUnderRepository -RepositoryRoot $repoRoot -Path $fullPath

    if (-not (Test-Path -LiteralPath $fullPath)) {
        return $false
    }

    if ($PSCmdlet.ShouldProcess($fullPath, "Remove generated path")) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }

    return $true
}

function Test-OnlyRagSkippedTree {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $relativePath = [System.IO.Path]::GetRelativePath($repoRoot, [System.IO.Path]::GetFullPath($Path))
    return (
        $relativePath -eq ".git" -or
        $relativePath.StartsWith(".git\", [System.StringComparison]::OrdinalIgnoreCase) -or
        $relativePath -eq "certificates" -or
        $relativePath.StartsWith("certificates\", [System.StringComparison]::OrdinalIgnoreCase) -or
        $relativePath -eq "src\OnlyRag.Web\node_modules" -or
        $relativePath.StartsWith("src\OnlyRag.Web\node_modules\", [System.StringComparison]::OrdinalIgnoreCase) -or
        $relativePath -eq "artifacts" -or
        $relativePath.StartsWith("artifacts\", [System.StringComparison]::OrdinalIgnoreCase)
    )
}

function Test-OnlyRagPreservedIgnoredStatusLine {
    param(
        [Parameter(Mandatory)]
        [string]$Line
    )

    if (-not $Line.StartsWith("!! ", [System.StringComparison]::Ordinal)) {
        return $false
    }

    $relativePath = $Line.Substring(3).Trim().TrimEnd('/').Replace('/', '\')
    return (
        (-not $cleanArtifacts -and ($relativePath -eq "artifacts" -or $relativePath.StartsWith("artifacts\", [System.StringComparison]::OrdinalIgnoreCase))) -or
        (-not $cleanDependencies -and ($relativePath -eq "src\OnlyRag.Web\node_modules" -or $relativePath.StartsWith("src\OnlyRag.Web\node_modules\", [System.StringComparison]::OrdinalIgnoreCase)))
    )
}

function Remove-OnlyRagDirectorySet {
    param(
        [Parameter(Mandatory)]
        [string]$Name
    )

    Get-ChildItem -LiteralPath $repoRoot -Directory -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq $Name -and -not (Test-OnlyRagSkippedTree -Path $_.FullName) } |
        ForEach-Object {
            [void](Remove-OnlyRagPathIfExists -Path $_.FullName)
        }
}

function Remove-OnlyRagFileSet {
    param(
        [Parameter(Mandatory)]
        [string]$Filter
    )

    Get-ChildItem -LiteralPath $repoRoot -File -Recurse -Force -Filter $Filter -ErrorAction SilentlyContinue |
        Where-Object { -not (Test-OnlyRagSkippedTree -Path $_.FullName) } |
        ForEach-Object {
            [void](Remove-OnlyRagPathIfExists -Path $_.FullName)
        }
}

Write-Host "OnlyRag clean" -ForegroundColor Cyan
Write-Host "Repository: $repoRoot"
Write-Host "Artifacts: $(if ($cleanArtifacts) { 'cleaned' } else { 'preserved' })"
Write-Host "Dependencies: $(if ($cleanDependencies) { 'cleaned' } else { 'preserved' })"

$explicitGeneratedPaths = @(
    "src\OnlyRag.Web\dist",
    "src\OnlyRag.Web\.vite",
    "src\OnlyRag.Web\playwright-report",
    "src\OnlyRag.Web\test-results",
    "src\OnlyRag.Web\output",
    "TestResults",
    "coverage",
    "playwright-report",
    "test-results",
    "output",
    "packaging\qdrant\payload"
)

foreach ($relativePath in $explicitGeneratedPaths) {
    $path = Resolve-OnlyRagRepositoryPath -RelativePath $relativePath
    [void](Remove-OnlyRagPathIfExists -Path $path)
}

Remove-OnlyRagDirectorySet -Name "bin"
Remove-OnlyRagDirectorySet -Name "obj"
Remove-OnlyRagDirectorySet -Name "__pycache__"
Remove-OnlyRagFileSet -Filter "*.tsbuildinfo"
Remove-OnlyRagFileSet -Filter "*.pyc"

if ($cleanArtifacts) {
    [void](Remove-OnlyRagPathIfExists -Path (Resolve-OnlyRagRepositoryPath -RelativePath "artifacts"))
}

if ($cleanDependencies) {
    [void](Remove-OnlyRagPathIfExists -Path (Resolve-OnlyRagRepositoryPath -RelativePath "src\OnlyRag.Web\node_modules"))
}

if ($WhatIfPreference) {
    Write-Host "WhatIf completed. Repository cleanliness was not enforced because no files were removed." -ForegroundColor Yellow
    return
}

$gitCommand = Get-Command "git" -ErrorAction SilentlyContinue
if ($gitCommand) {
    $statusLines = & $gitCommand.Source -C $repoRoot status --short --ignored
    if ($LASTEXITCODE -ne 0) {
        throw "git status failed with exit code $LASTEXITCODE."
    }

    $ignoredGeneratedLines = @($statusLines | Where-Object {
        $_.StartsWith("!! ", [System.StringComparison]::Ordinal) -and
        -not (Test-OnlyRagPreservedIgnoredStatusLine -Line $_)
    })
    if ($ignoredGeneratedLines.Count -gt 0) {
        Write-Host "Remaining ignored/generated paths:" -ForegroundColor Red
        $ignoredGeneratedLines | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        throw "Clean completed but ignored/generated paths remain."
    }

    $sourceChangeLines = @($statusLines | Where-Object { -not $_.StartsWith("!! ", [System.StringComparison]::Ordinal) })
    if ($sourceChangeLines.Count -gt 0) {
        Write-Warning "Generated outputs are clean, but the repository still has source changes. Clean.ps1 does not revert tracked or untracked source files."
        $sourceChangeLines | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
    }
    else {
        Write-Host "Repository clean." -ForegroundColor Green
    }
}
else {
    Write-Warning "git was not found; skipped repository cleanliness verification."
}

Write-Host "Clean completed." -ForegroundColor Green
