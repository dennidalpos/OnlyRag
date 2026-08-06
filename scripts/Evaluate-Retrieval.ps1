param(
    [string]$DatasetPath = "docs\retrieval-evaluation.sample.json",

    [switch]$GenerateSyntheticDataset,

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

    $dcgAtK = 0.0
    $hitsCount = 0
    $sumPrecision = 0.0

    for ($index = 0; $index -lt $returnedChunkIds.Count; $index++) {
        $rank = $index + 1
        if ($expectedChunkIds -contains $returnedChunkIds[$index]) {
            $hitsCount++
            $sumPrecision += ($hitsCount / [double]$rank)
            $dcgAtK += (1.0 / [Math]::Log2($rank + 1))
        }
    }

    $maxPossibleHits = [Math]::Min($CaseTopK, $expectedChunkIds.Count)
    $apAtK = if ($maxPossibleHits -gt 0) { $sumPrecision / [double]$maxPossibleHits } else { 0.0 }

    $idcgAtK = 0.0
    for ($rank = 1; $rank -le $maxPossibleHits; $rank++) {
        $idcgAtK += (1.0 / [Math]::Log2($rank + 1))
    }

    $ndcgAtK = if ($idcgAtK -gt 0.0) { $dcgAtK / $idcgAtK } else { 0.0 }

    return [ordered]@{
        id = [string]$Case.id
        query = [string]$Case.query
        topK = $CaseTopK
        expectedChunkIds = $expectedChunkIds
        returnedChunkIds = $returnedChunkIds
        hitChunkIds = $hits
        recallAtK = [Math]::Round($recallAtK, 4)
        reciprocalRank = [Math]::Round($reciprocalRank, 4)
        apAtK = [Math]::Round($apAtK, 4)
        ndcgAtK = [Math]::Round($ndcgAtK, 4)
        firstRelevantRank = $firstRelevantRank
        contextCharacters = $contextCharacters
        maxContextCharacters = $Response.maxContextCharacters
        keywordBackend = $Response.keywordBackend
        vectorBackend = $Response.vectorBackend
    }
}

if ($GenerateSyntheticDataset) {
    Write-Host "Generazione dataset sintetico di valutazione RAG in corso..." -ForegroundColor Cyan
    $syntheticDirectory = Split-Path -Parent $DatasetPath
    if (-not [string]::IsNullOrWhiteSpace($syntheticDirectory) -and -not (Test-Path -LiteralPath $syntheticDirectory)) {
        New-Item -ItemType Directory -Path $syntheticDirectory -Force | Out-Null
    }

    $sampleCases = @(
        [ordered]@{
            id = "synth_case_1"
            query = "Quali sono i requisiti di sistema e la versione .NET di OnlyRag?"
            documentIds = @(1)
            expectedChunkIds = @(101, 102)
        },
        [ordered]@{
            id = "synth_case_2"
            query = "Come viene effettuata la valutazione CRAG Self-Corrective RAG?"
            documentIds = @(1)
            expectedChunkIds = @(201)
        }
    )

    $syntheticDataset = [ordered]@{
        name = "Synthetic Auto-Generated Evaluation Set"
        topK = $TopK
        cases = $sampleCases
    }

    $syntheticDataset | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $DatasetPath -Encoding utf8
    Write-Host "Dataset sintetico generato con successo: $DatasetPath" -ForegroundColor Green
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
$mapAtK = ($evaluatedCases | ForEach-Object { [double]$_["apAtK"] } | Measure-Object -Average).Average
$ndcgAtK = ($evaluatedCases | ForEach-Object { [double]$_["ndcgAtK"] } | Measure-Object -Average).Average
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
        mapAtK = [Math]::Round([double]$mapAtK, 4)
        ndcgAtK = [Math]::Round([double]$ndcgAtK, 4)
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
Write-Host "recall@$defaultTopK=$($report.summary.recallAtK) mrr=$($report.summary.mrr) map@$defaultTopK=$($report.summary.mapAtK) ndcg@$defaultTopK=$($report.summary.ndcgAtK) avgContextChars=$($report.summary.averageContextCharacters)"
