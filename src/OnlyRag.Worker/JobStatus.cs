namespace OnlyRag.Worker;

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
