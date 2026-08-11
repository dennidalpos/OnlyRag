using NSubstitute;
using OnlyRag.Application.Jobs;
using OnlyRag.Jobs.Abstractions;

namespace OnlyRag.Application.Tests;

public sealed class JobApplicationServiceTests
{
    [Fact]
    public async Task CancelAsync_CancelsQueueJobAndRunningExecution()
    {
        ILocalJobQueue queue = Substitute.For<ILocalJobQueue>();
        RunningJobCancellationRegistry registry = new();
        using CancellationTokenSource applicationStopping = new();
        using CancellationTokenSource running = registry.Register("job-1", applicationStopping.Token);
        LocalJob job = CreateJob("job-1", JobStatus.Running);
        queue.CancelAsync("job-1", Arg.Any<CancellationToken>()).Returns(job);

        JobApplicationService service = new(queue, registry);
        LocalJob? result = await service.CancelAsync("job-1", CancellationToken.None);

        Assert.Same(job, result);
        Assert.True(running.IsCancellationRequested);
        await queue.Received(1).CancelAsync("job-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeAsync_ReturnsConflictWhilePauseIsCompleting()
    {
        ILocalJobQueue queue = Substitute.For<ILocalJobQueue>();
        RunningJobCancellationRegistry registry = new();
        LocalJob job = CreateJob("job-2", JobStatus.Pausing);
        queue.GetAsync("job-2", Arg.Any<CancellationToken>()).Returns(job);

        JobResumeResult result = await new JobApplicationService(queue, registry)
            .ResumeAsync("job-2", CancellationToken.None);

        Assert.Equal("job_pause_in_progress", result.ConflictCode);
        Assert.Null(result.Job);
        await queue.DidNotReceive().ResumeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_RejectsActiveJobBeforeTouchingQueue()
    {
        ILocalJobQueue queue = Substitute.For<ILocalJobQueue>();
        LocalJob job = CreateJob("job-3", JobStatus.Pending);
        queue.GetAsync("job-3", Arg.Any<CancellationToken>()).Returns(job);

        JobDeleteResult result = await new JobApplicationService(queue, new RunningJobCancellationRegistry())
            .DeleteAsync("job-3", CancellationToken.None);

        Assert.Equal(JobDeleteResult.Active, result);
        await queue.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelAndWaitAsync_CancelsAndWaitsForRunningExecution()
    {
        ILocalJobQueue queue = Substitute.For<ILocalJobQueue>();
        RunningJobCancellationRegistry registry = new();
        LocalJob job = CreateJob("job-4", JobStatus.Running);
        queue.GetAsync("job-4", Arg.Any<CancellationToken>()).Returns(job);

        using CancellationTokenSource running = registry.Register("job-4", CancellationToken.None);
        Task unregister = Task.Run(async () =>
        {
            while (!running.IsCancellationRequested)
            {
                await Task.Delay(10);
            }

            registry.Unregister("job-4");
        });

        await new JobApplicationService(queue, registry)
            .CancelAndWaitAsync("job-4", CancellationToken.None);
        await unregister;

        await queue.Received(1).CancelAsync("job-4", Arg.Any<CancellationToken>());
        Assert.False(registry.IsRunning("job-4"));
    }

    private static LocalJob CreateJob(string id, JobStatus status)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new LocalJob(
            id,
            "test",
            status,
            0,
            0,
            string.Empty,
            "{}",
            "{}",
            null,
            0,
            3,
            null,
            now,
            now);
    }
}
