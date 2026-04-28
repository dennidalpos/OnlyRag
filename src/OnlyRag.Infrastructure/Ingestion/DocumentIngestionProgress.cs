namespace OnlyRag.Infrastructure.Ingestion;

public sealed record DocumentIngestionProgress(
    int ProgressPercent,
    string CurrentStep,
    DocumentIngestionCheckpoint Checkpoint);
