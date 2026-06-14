param(
    [Parameter(Mandatory = $true)]
    [string]$DatasetPath,

    [string]$OutputPath = "artifacts\retrieval-evaluation\report.json",

    [string]$BackendBaseUrl,

    [string]$SessionToken,

    [int]$TopK = 5
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Read-JsonFile {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "JSON file not found: $Path"
    }

    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function ConvertTo-Array {
    param($Value)

    if ($null -eq $Value) {
        return @()
    }

    if ($Value -is [System.Array]) {
        return @($Value)
    }

    return @($Value)
}

function Invoke-RetrievalSearch {
    param(
        [string]$BaseUrl,
        [string]$Token,
        [object]$Case,
        [int]$CaseTopK
    )

    $headers = @{}
    if (-not [string]::IsNullOrWhiteSpace($Token)) {
        $headers["X-OnlyRag-Session-Token"] = $Token
    }

    $body = @{
        query = [string]$Case.query
        documentIds = @(ConvertTo-Array $Case.documentIds | ForEach-Object { [int64]$_ })
        topK = $CaseTopK
    } | ConvertTo-Json -Depth 8

    $uri = ([System.Uri]::new(([System.Uri]::new($BaseUrl)), "/api/search")).AbsoluteUri
    return Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -Body $body -ContentType "application/json"
}

function Measure-Case {
    param(
        [object]$Case,
        [object]$Response,
        [int]$CaseTopK
    )

    $expectedChunkIds = @(ConvertTo-Array $Case.expectedChunkIds | ForEach-Object { [int64]$_ })
    if ($expectedChunkIds.Count -eq 0) {
        throw "Case '$($Case.id)' must define at least one expectedChunkIds value."
    }

    $results = @(ConvertTo-Array $Response.results | Select-Object -First $CaseTopK)
    $returnedChunkIds = @($results | ForEach-Object { [int64]$_.chunkId })
    $hits = @($expectedChunkIds | Where-Object { $returnedChunkIds -contains $_ })
    $firstRelevantRank = $null
    for ($index = 0; $index -lt $returnedChunkIds.Count; $index++) {
        if ($expectedChunkIds -contains $returnedChunkIds[$index]) {
            $firstRelevantRank = $index + 1
            break
        }
    }

    $contextCharacters = 0
    foreach ($result in $results) {
        $contextCharacters += ([string]$result.snippet).Length
    }

    $recallAtK = $hits.Count / [double]$expectedChunkIds.Count
    $reciprocalRank = if ($null -eq $firstRelevantRank) { 0.0 } else { 1.0 / [double]$firstRelevantRank }

    return [ordered]@{
        id = [string]$Case.id
        query = [string]$Case.query
        topK = $CaseTopK
        expectedChunkIds = $expectedChunkIds
        returnedChunkIds = $returnedChunkIds
        hitChunkIds = $hits
        recallAtK = [Math]::Round($recallAtK, 4)
        reciprocalRank = [Math]::Round($reciprocalRank, 4)
        firstRelevantRank = $firstRelevantRank
        contextCharacters = $contextCharacters
        maxContextCharacters = $Response.maxContextCharacters
        keywordBackend = $Response.keywordBackend
        vectorBackend = $Response.vectorBackend
    }
}

$dataset = Read-JsonFile -Path $DatasetPath
$cases = @(ConvertTo-Array $dataset.cases)
if ($cases.Count -eq 0) {
    throw "Dataset must contain a non-empty cases array."
}

$defaultTopK = if ($dataset.PSObject.Properties.Name -contains "topK") { [int]$dataset.topK } else { $TopK }
$evaluatedCases = New-Object System.Collections.Generic.List[object]
foreach ($case in $cases) {
    $caseTopK = if ($case.PSObject.Properties.Name -contains "topK") { [int]$case.topK } else { $defaultTopK }
    if ([string]::IsNullOrWhiteSpace([string]$case.id)) {
        throw "Every retrieval evaluation case must define an id."
    }

    if ([string]::IsNullOrWhiteSpace([string]$case.query)) {
        throw "Case '$($case.id)' must define a query."
    }

    $response = $null
    if (-not [string]::IsNullOrWhiteSpace($BackendBaseUrl)) {
        $response = Invoke-RetrievalSearch -BaseUrl $BackendBaseUrl -Token $SessionToken -Case $case -CaseTopK $caseTopK
    }
    elseif ($case.PSObject.Properties.Name -contains "results") {
        $response = [pscustomobject]@{
            results = $case.results
            maxContextCharacters = $case.maxContextCharacters
            keywordBackend = $case.keywordBackend
            vectorBackend = $case.vectorBackend
        }
    }
    else {
        throw "Case '$($case.id)' must include results when BackendBaseUrl is not supplied."
    }

    $evaluatedCases.Add((Measure-Case -Case $case -Response $response -CaseTopK $caseTopK))
}

$averageRecall = ($evaluatedCases | ForEach-Object { [double]$_["recallAtK"] } | Measure-Object -Average).Average
$mrr = ($evaluatedCases | ForEach-Object { [double]$_["reciprocalRank"] } | Measure-Object -Average).Average
$averageContextCharacters = ($evaluatedCases | ForEach-Object { [double]$_["contextCharacters"] } | Measure-Object -Average).Average
$evaluatedCaseArray = $evaluatedCases.ToArray()

$report = [ordered]@{
    evaluatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    datasetPath = (Resolve-Path -LiteralPath $DatasetPath).Path
    topK = $defaultTopK
    caseCount = $evaluatedCases.Count
    summary = [ordered]@{
        recallAtK = [Math]::Round([double]$averageRecall, 4)
        mrr = [Math]::Round([double]$mrr, 4)
        averageContextCharacters = [Math]::Round([double]$averageContextCharacters, 2)
    }
    cases = $evaluatedCaseArray
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$report | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Host "Retrieval evaluation written to $OutputPath"
Write-Host "recall@$defaultTopK=$($report.summary.recallAtK) mrr=$($report.summary.mrr) avgContextChars=$($report.summary.averageContextCharacters)"
