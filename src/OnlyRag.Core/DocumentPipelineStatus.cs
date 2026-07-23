namespace OnlyRag.Core;

public enum PipelinePhase
{
    Import,
    Analysis,
    Ocr,
    TextExtraction,
    Chunking,
    Embedding,
    Ready
}

public enum PhaseState
{
    Todo,
    InProgress,
    Completed,
    Skipped,
    Failed
}

public sealed record PipelinePhaseInfo(
    PhaseState State,
    string? Error,
    DateTimeOffset? CompletedAtUtc);

public sealed record DocumentPipelineStatus(
    long DocumentId,
    string OcrPolicy,
    PipelinePhaseInfo Import,
    PipelinePhaseInfo Analysis,
    PipelinePhaseInfo Ocr,
    PipelinePhaseInfo TextExtraction,
    PipelinePhaseInfo Chunking,
    PipelinePhaseInfo Embedding,
    PhaseState OverallState,
    string? ActiveJobId,
    string? ActiveJobType);
