using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Worker;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    private static void MapJobEndpoints(WebApplication app)
    {
        app.MapGet("/api/jobs", async (int? limit, ILocalJobQueue jobs, CancellationToken cancellationToken) =>
            Results.Ok(await jobs.ListAsync(limit, cancellationToken)));

        app.MapGet("/api/jobs/{id}", async (string id, ILocalJobQueue jobs, CancellationToken cancellationToken) =>
        {
            LocalJob? job = await jobs.GetAsync(id, cancellationToken);
            return job is null ? Results.NotFound() : Results.Ok(job);
        });

        app.MapPost("/api/jobs/{id}/cancel", async (
            string id,
            ILocalJobQueue jobs,
            RunningJobCancellationRegistry cancellationRegistry,
            CancellationToken cancellationToken) =>
        {
            LocalJob? job = await jobs.CancelAsync(id, cancellationToken);
            if (job is null)
            {
                return Results.NotFound();
            }

            cancellationRegistry.Cancel(id);
            return Results.Ok(job);
        });

        app.MapPost("/api/jobs/{id}/pause", async (
            string id,
            ILocalJobQueue jobs,
            RunningJobCancellationRegistry cancellationRegistry,
            CancellationToken cancellationToken) =>
        {
            LocalJob? job = await jobs.PauseAsync(id, cancellationToken);
            if (job is null)
            {
                return Results.NotFound();
            }

            cancellationRegistry.Cancel(id);
            return Results.Ok(job);
        });

        app.MapPost("/api/jobs/{id}/resume", async (string id, ILocalJobQueue jobs, CancellationToken cancellationToken) =>
        {
            LocalJob? job = await jobs.ResumeAsync(id, cancellationToken);
            return job is null ? Results.NotFound() : Results.Ok(job);
        });

        app.MapDelete("/api/jobs/{id}", async (string id, ILocalJobQueue jobs, CancellationToken cancellationToken) =>
        {
            LocalJob? job = await jobs.GetAsync(id, cancellationToken);
            if (job is null)
            {
                return Results.NotFound();
            }

            if (job.Status is JobStatus.Running or JobStatus.Pending)
            {
                return Results.Conflict("Impossibile eliminare un job in esecuzione o in attesa.");
            }

            bool deleted = await jobs.DeleteAsync(id, cancellationToken);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        app.MapDelete("/api/jobs", async (ILocalJobQueue jobs, CancellationToken cancellationToken) =>
        {
            int deleted = await jobs.PurgeCompletedAsync(cancellationToken);
            return Results.Ok(new { deleted });
        });
    }
}
