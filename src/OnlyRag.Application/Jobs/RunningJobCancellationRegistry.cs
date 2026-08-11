using System.Collections.Concurrent;

namespace OnlyRag.Application.Jobs;

public sealed class RunningJobCancellationRegistry
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> runningJobs = new(StringComparer.Ordinal);

    public CancellationTokenSource Register(string jobId, CancellationToken applicationStoppingToken)
    {
        CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(applicationStoppingToken);
        runningJobs[jobId] = cancellation;
        return cancellation;
    }

    public void Cancel(string jobId)
    {
        if (runningJobs.TryGetValue(jobId, out CancellationTokenSource? cancellation))
        {
            cancellation.Cancel();
        }
    }

    public bool IsRunning(string jobId) => runningJobs.ContainsKey(jobId);

    public IReadOnlyList<string> ListRunningJobIds() => runningJobs.Keys.ToArray();

    public void Unregister(string jobId)
    {
        if (runningJobs.TryRemove(jobId, out CancellationTokenSource? cancellation))
        {
            cancellation.Dispose();
        }
    }
}
