using Microsoft.AspNetCore.SignalR;
using OnlyRag.Api.Hubs;
using OnlyRag.Worker;

namespace OnlyRag.Api;

internal sealed class SignalRJobProgressNotifier : IJobProgressNotifier
{
    // All clients join this group to receive global job events (e.g. for job list refresh)
    internal const string GlobalJobsGroup = "global-jobs";

    private readonly IHubContext<JobProgressHub, IJobProgressClient> _hubContext;

    public SignalRJobProgressNotifier(IHubContext<JobProgressHub, IJobProgressClient> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifyProgressAsync(string jobId, string jobType, int progressPercent, string status, string? stepMessage)
    {
        await _hubContext.Clients.Group(jobId).JobProgressUpdated(jobId, jobType, progressPercent, status, stepMessage);
        await _hubContext.Clients.Group(GlobalJobsGroup).JobProgressUpdated(jobId, jobType, progressPercent, status, stepMessage);
    }

    public async Task NotifyCompletedAsync(string jobId, string jobType)
    {
        await _hubContext.Clients.Group(jobId).JobCompleted(jobId, jobType);
        await _hubContext.Clients.Group(GlobalJobsGroup).JobCompleted(jobId, jobType);
    }

    public async Task NotifyFailedAsync(string jobId, string jobType, string error)
    {
        await _hubContext.Clients.Group(jobId).JobFailed(jobId, jobType, error);
        await _hubContext.Clients.Group(GlobalJobsGroup).JobFailed(jobId, jobType, error);
    }
}
