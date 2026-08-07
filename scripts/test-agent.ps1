<#
.SYNOPSIS
    Agent fast-mode test runner (AGENTS.md compliant).
    Runs PASS/FAIL summary only. No full integration suite.

.PARAMETER Full
    Switch to run the complete test suite (manual/debugging mode only).
#>
param([switch]$Full)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$script:failed = 0

function Invoke-DotnetTest {
    param([string]$Project, [string]$Filter = '')
    $testArgs = @(
        'test', $Project,
        '--configuration', 'Release',
        '--nologo',
        '--logger', 'console;verbosity=minimal'
    )
    if ($Filter) { $testArgs += '--filter'; $testArgs += $Filter }
    & dotnet @testArgs | Select-Object -Last 5
    if ($LASTEXITCODE -ne 0) { $script:failed++ }
}

Write-Host "`n=== OnlyRag.Core.Tests ===" -ForegroundColor Cyan
Invoke-DotnetTest "$root\tests\OnlyRag.Core.Tests\OnlyRag.Core.Tests.csproj"

Write-Host "`n=== OnlyRag.Infrastructure.Tests ===" -ForegroundColor Cyan
Invoke-DotnetTest "$root\tests\OnlyRag.Infrastructure.Tests\OnlyRag.Infrastructure.Tests.csproj"

Write-Host "`n=== OnlyRag.Api.Tests (fast only) ===" -ForegroundColor Cyan

# Fast mode: include only unit and lightweight integration tests.
# Excluded: InProcessBackend*, Document*JobHandler*, LocalJobWorker*,
#           ChatServiceQdrant*, OllamaModelPull*, QdrantProcessLifetime*,
#           Diagnostics*Tests, RerankerEndpoints* (all use InProcessBackend.StartAsync)
$fastFilter = @(
    'FullyQualifiedName~OnlyRag.Api.Tests.MicrosoftExtensionsAiIntegration',
    'FullyQualifiedName~OnlyRag.Api.Tests.CloudLlmIntegration',
    'FullyQualifiedName~OnlyRag.Api.Tests.OcrProvision',
    'FullyQualifiedName~OnlyRag.Api.Tests.OcrRuntimeEnvironment',
    'FullyQualifiedName~OnlyRag.Api.Tests.UserFacingErrorText',
    'FullyQualifiedName~OnlyRag.Api.Tests.AgentCycleGuard',
    'FullyQualifiedName~OnlyRag.Api.Tests.AgentLoopEngine',
    'FullyQualifiedName~OnlyRag.Api.Tests.ChatServiceTests',
    'FullyQualifiedName~OnlyRag.Api.Tests.EndToEndIntegration',
    'FullyQualifiedName~OnlyRag.Api.Tests.McpSchemaValidator',
    'FullyQualifiedName~OnlyRag.Api.Tests.McpSseClientService',
    'FullyQualifiedName~OnlyRag.Api.Tests.DiagnosticsProbeCache',
    'FullyQualifiedName~OnlyRag.Api.Tests.OllamaClientTests',
    'FullyQualifiedName~OnlyRag.Api.Tests.SubagentRunner',
    'FullyQualifiedName~OnlyRag.Api.Tests.TaskAndCommandToolHandler'
) -join '|'

if ($Full) {
    Write-Host "(FULL mode — running all Api.Tests)" -ForegroundColor Yellow
    Invoke-DotnetTest "$root\tests\OnlyRag.Api.Tests\OnlyRag.Api.Tests.csproj"
}
else {
    Invoke-DotnetTest "$root\tests\OnlyRag.Api.Tests\OnlyRag.Api.Tests.csproj" $fastFilter
}

Write-Host ""
if ($script:failed -eq 0) {
    Write-Host "ALL PASS" -ForegroundColor Green
} else {
    Write-Host "FAIL — $($script:failed) suite(s) failed" -ForegroundColor Red
    exit 1
}
