#requires -Version 7.0
[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$IncludeArtifacts,

    [switch]$IncludeDependencies
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$supportScript = Join-Path $PSScriptRoot "support\BuildSupport.ps1"
. $supportScript

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

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
        ((-not $IncludeArtifacts) -and (
            $relativePath -eq "artifacts" -or
            $relativePath.StartsWith("artifacts\", [System.StringComparison]::OrdinalIgnoreCase)
        ))
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
Write-Host "Artifacts: $(if ($IncludeArtifacts) { 'included' } else { 'skipped' })"
Write-Host "Dependencies: $(if ($IncludeDependencies) { 'included' } else { 'skipped' })"

$explicitGeneratedPaths = @(
    "src\OnlyRag.Web\dist",
    "src\OnlyRag.Web\.vite",
    "TestResults",
    "coverage",
    "output"
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

if ($IncludeArtifacts) {
    [void](Remove-OnlyRagPathIfExists -Path (Resolve-OnlyRagRepositoryPath -RelativePath "artifacts"))
}

if ($IncludeDependencies) {
    [void](Remove-OnlyRagPathIfExists -Path (Resolve-OnlyRagRepositoryPath -RelativePath "src\OnlyRag.Web\node_modules"))
}

Write-Host "Clean completed." -ForegroundColor Green
