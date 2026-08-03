using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Core;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    internal static void MapMultiAgentEndpoints(this WebApplication app)
    {
        app.MapPost("/api/agent/orchestrate", async (
            MultiAgentOrchestrationRequest request,
            IMultiAgentOrchestratorService orchestrator,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.OverallGoal))
            {
                return Results.BadRequest(new { title = "Obiettivo non valido", detail = "Specificare un obiettivo valido per l'orchestrazione." });
            }

            var status = await orchestrator.StartOrchestrationAsync(request, cancellationToken);
            return Results.Ok(status);
        });

        app.MapGet("/api/agent/orchestrate/{id}", async (
            string id,
            IMultiAgentOrchestratorService orchestrator,
            CancellationToken cancellationToken) =>
        {
            var status = await orchestrator.GetStatusAsync(id, cancellationToken);
            return status is not null ? Results.Ok(status) : Results.NotFound();
        });
    }
}
