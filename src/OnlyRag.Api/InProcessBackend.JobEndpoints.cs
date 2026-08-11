using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Application.Jobs;
using OnlyRag.Jobs.Abstractions;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    internal static void MapJobEndpoints(this WebApplication app)
    {
        app.MapGet("/api/jobs", async (int? limit, JobApplicationService jobs, CancellationToken cancellationToken) =>
            Results.Ok(await jobs.ListAsync(limit, cancellationToken)));

        app.MapGet("/api/jobs/{id}", async (string id, JobApplicationService jobs, CancellationToken cancellationToken) =>
        {
            LocalJob? job = await jobs.GetAsync(id, cancellationToken);
            return job is null ? CreateNotFoundProblem("Job") : Results.Ok(job);
        });

        app.MapPost("/api/jobs/{id}/cancel", async (
            string id,
            JobApplicationService jobs,
            CancellationToken cancellationToken) =>
        {
            LocalJob? job = await jobs.CancelAsync(id, cancellationToken);
            if (job is null)
            {
                return CreateNotFoundProblem("Job");
            }

            return Results.Ok(job);
        });

        app.MapPost("/api/jobs/{id}/pause", async (
            string id,
            JobApplicationService jobs,
            CancellationToken cancellationToken) =>
        {
            LocalJob? job = await jobs.PauseAsync(id, cancellationToken);
            if (job is null)
            {
                return CreateNotFoundProblem("Job");
            }

            return Results.Ok(job);
        });

        app.MapPost("/api/jobs/{id}/resume", async (
            string id,
            JobApplicationService jobs,
            CancellationToken cancellationToken) =>
        {
            JobResumeResult result = await jobs.ResumeAsync(id, cancellationToken);
            if (result.ConflictCode == "job_pause_in_progress")
            {
                return CreateConflictProblem(
                    "Job paused",
                    "The job is still completing the pause. Please try again shortly.",
                    "job_pause_in_progress"                    );
            }

            LocalJob? job = result.Job;
            return job is null ? CreateNotFoundProblem("Job") : Results.Ok(job);
        });

        app.MapDelete("/api/jobs/{id}", async (string id, JobApplicationService jobs, CancellationToken cancellationToken) =>
        {
            return (await jobs.DeleteAsync(id, cancellationToken)) switch
            {
                    JobDeleteResult.Deleted => Results.NoContent(),
                    JobDeleteResult.Active => CreateConflictProblem(
                        "Active job",
                        "Cannot delete a running or pending job.",
                        "job_active"),
                    _ => CreateNotFoundProblem("Job")
            };
        });

        app.MapDelete("/api/jobs", async (JobApplicationService jobs, CancellationToken cancellationToken) =>
        {
            int deleted = await jobs.PurgeCompletedAsync(cancellationToken);
            return Results.Ok(new { deleted });
        });
    }
}
