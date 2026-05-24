#requires -Version 7.0

function New-OnlyRagPrerequisiteMessage {
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

    return "Missing prerequisite: $Software. Minimum supported version: $MinimumVersion. Why it is required: $WhyRequired. Instruction: $Instruction. Verify: $Verify"
}

function ConvertTo-OnlyRagVersionOrNull {
    param([string]$Text)

    if ($Text -match 'v?(\d+)\.(\d+)\.(\d+)') {
        return [version]::new([int]$Matches[1], [int]$Matches[2], [int]$Matches[3])
    }

    return $null
}

function Test-OnlyRagNodeSupportedVersion {
    param([version]$Version)

    return (
        ($Version.Major -eq 20 -and $Version.Minor -ge 19) -or
        ($Version.Major -eq 22 -and $Version.Minor -ge 12) -or
        ($Version.Major -gt 22)
    )
}

function Assert-OnlyRagDotNetSdk {
    $dotnetCommand = Get-Command "dotnet" -ErrorAction SilentlyContinue
    if (-not $dotnetCommand) {
        throw (New-OnlyRagPrerequisiteMessage `
            -Software ".NET SDK" `
            -MinimumVersion ".NET 10 SDK for Windows, matching global.json 10.0.300 with latestFeature roll-forward" `
            -WhyRequired "OnlyRag builds and publishes a .NET 10 WPF desktop app and in-process backend" `
            -Instruction "Install the official .NET 10 SDK for Windows from https://dotnet.microsoft.com/download/dotnet/10.0, then rerun the command" `
            -Verify "Run dotnet --list-sdks and confirm a 10.x SDK is listed")
    }

    $sdks = @(& $dotnetCommand.Source --list-sdks)
    $sdk10 = @($sdks | Where-Object { $_ -match '^10\.' })
    if ($sdk10.Count -eq 0) {
        throw (New-OnlyRagPrerequisiteMessage `
            -Software ".NET SDK" `
            -MinimumVersion ".NET 10 SDK for Windows, matching global.json 10.0.300 with latestFeature roll-forward" `
            -WhyRequired "OnlyRag builds and publishes a .NET 10 WPF desktop app and in-process backend" `
            -Instruction "Install the official .NET 10 SDK for Windows from https://dotnet.microsoft.com/download/dotnet/10.0, then rerun the command" `
            -Verify "Run dotnet --list-sdks and confirm a 10.x SDK is listed")
    }

    return $dotnetCommand
}

function Assert-OnlyRagNodeToolchain {
    $nodeCommand = Get-Command "node" -ErrorAction SilentlyContinue
    if (-not $nodeCommand) {
        throw (New-OnlyRagPrerequisiteMessage `
            -Software "Node.js with npm" `
            -MinimumVersion "Node.js 20.19.x or 22.12+ with npm" `
            -WhyRequired "OnlyRag builds the bundled React/Vite UI before desktop build and installer packaging" `
            -Instruction "Install the official Node.js for Windows release from https://nodejs.org/ with npm included, then rerun the command" `
            -Verify "Run node --version and npm --version")
    }

    $nodeVersionText = (& $nodeCommand.Source --version 2>&1 | Out-String).Trim()
    $nodeVersion = ConvertTo-OnlyRagVersionOrNull $nodeVersionText
    if (-not $nodeVersion -or -not (Test-OnlyRagNodeSupportedVersion -Version $nodeVersion)) {
        throw (New-OnlyRagPrerequisiteMessage `
            -Software "Node.js" `
            -MinimumVersion "Node.js 20.19.x or 22.12+ as declared by src\OnlyRag.Web\package.json" `
            -WhyRequired "OnlyRag uses Vite 7 and TypeScript tooling to build the bundled WebView2 UI" `
            -Instruction "Install or select a supported official Node.js for Windows version, then rerun the command" `
            -Verify "Run node --version and confirm it is 20.19.x, 22.12+, or newer")
    }

    $npmCommand = Get-Command "npm" -ErrorAction SilentlyContinue
    if (-not $npmCommand) {
        throw (New-OnlyRagPrerequisiteMessage `
            -Software "npm" `
            -MinimumVersion "npm bundled with supported Node.js 20.19.x or 22.12+" `
            -WhyRequired "OnlyRag restores frontend dependencies from src\OnlyRag.Web\package-lock.json" `
            -Instruction "Install the official Node.js for Windows package with npm included, then rerun the command" `
            -Verify "Run npm --version from PowerShell")
    }

    return [pscustomobject]@{
        Node = $nodeCommand
        Npm = $npmCommand
        NodeVersionText = $nodeVersionText
    }
}
