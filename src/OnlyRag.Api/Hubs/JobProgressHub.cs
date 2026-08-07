using Microsoft.AspNetCore.SignalR;

namespace OnlyRag.Api.Hubs;

public interface IJobProgressClient
{
    Task JobProgressUpdated(string jobId, string jobType, int progressPercent, string status, string? stepMessage);
    Task JobCompleted(string jobId, string jobType);
    Task JobFailed(string jobId, string jobType, string error);
}

public sealed class JobProgressHub : Hub<IJobProgressClient>
{
    public async Task SubscribeJob(string jobId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, jobId);
    }

    public async Task UnsubscribeJob(string jobId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, jobId);
    }

    /// <summary>Subscribes this client to all job events (for global job list refresh).</summary>
    public async Task SubscribeGlobalJobs()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, SignalRJobProgressNotifier.GlobalJobsGroup);
    }

    /// <summary>Unsubscribes this client from global job events.</summary>
    public async Task UnsubscribeGlobalJobs()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, SignalRJobProgressNotifier.GlobalJobsGroup);
    }
}
