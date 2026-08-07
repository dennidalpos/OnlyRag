using Microsoft.AspNetCore.SignalR;
using OnlyRag.Api.Hubs;
using OnlyRag.Worker;

namespace OnlyRag.Api;

internal sealed class SignalRJobProgressNotifier : IJobProgressNotifier
{
    private readonly IHubContext<JobProgressHub, IJobProgressClient> _hubContext;

    public SignalRJobProgressNotifier(IHubContext<JobProgressHub, IJobProgressClient> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifyProgressAsync(string jobId, string jobType, int progressPercent, string status, string? stepMessage)
    {
        await _hubContext.Clients.All.JobProgressUpdated(jobId, jobType, progressPercent, status, stepMessage);
        await _hubContext.Clients.Group(jobId).JobProgressUpdated(jobId, jobType, progressPercent, status, stepMessage);
    }

    public async Task NotifyCompletedAsync(string jobId, string jobType)
    {
        await _hubContext.Clients.All.JobCompleted(jobId, jobType);
        await _hubContext.Clients.Group(jobId).JobCompleted(jobId, jobType);
    }

    public async Task NotifyFailedAsync(string jobId, string jobType, string error)
    {
        await _hubContext.Clients.All.JobFailed(jobId, jobType, error);
        await _hubContext.Clients.Group(jobId).JobFailed(jobId, jobType, error);
    }
}
