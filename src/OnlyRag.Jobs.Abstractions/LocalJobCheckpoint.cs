namespace OnlyRag.Jobs.Abstractions;

public sealed record LocalJobCheckpoint(
    int ProgressPercent,
    string CurrentStep,
    string CheckpointJson);
