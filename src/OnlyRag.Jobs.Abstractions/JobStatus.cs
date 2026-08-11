namespace OnlyRag.Jobs.Abstractions;

public enum JobStatus
{
    Pending,
    Running,
    Pausing,
    Completed,
    Failed,
    Cancelled,
    Paused
}
