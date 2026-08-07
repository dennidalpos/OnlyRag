namespace OnlyRag.Worker;

public interface IJobProgressNotifier
{
    Task NotifyProgressAsync(string jobId, string jobType, int progressPercent, string status, string? stepMessage);
    Task NotifyCompletedAsync(string jobId, string jobType);
    Task NotifyFailedAsync(string jobId, string jobType, string error);
}
