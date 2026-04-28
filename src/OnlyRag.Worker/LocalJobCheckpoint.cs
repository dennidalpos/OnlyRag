namespace OnlyRag.Worker;

public sealed record LocalJobCheckpoint(
    int ProgressPercent,
    string CurrentStep,
    string CheckpointJson);
