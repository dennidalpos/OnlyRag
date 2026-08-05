using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using OnlyRag.Core;
using OnlyRag.Core.Mcp;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    private static readonly JsonSerializerOptions AgentJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    internal static void MapAgentEndpoints(this WebApplication app)
    {
        app.MapPost("/api/agent/run-stream", async (
            HttpContext httpContext,
            [FromBody] AgentRunRequest request,
            AgentLoopEngine agentEngine) =>
        {
            if (string.IsNullOrWhiteSpace(request.Goal))
            {
                return CreateBadRequestProblem("Goal required", "The goal parameter cannot be empty.", "agent_goal_required");
            }

            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Connection = "keep-alive";

            try
            {
                await foreach (AgentStepEvent stepEvent in agentEngine.RunAgentLoopAsync(request, httpContext.RequestAborted))
                {
                    string eventJson = JsonSerializer.Serialize(stepEvent, AgentJsonOptions);
                    await httpContext.Response.WriteAsync($"data: {eventJson}\n\n", httpContext.RequestAborted);
                    await httpContext.Response.Body.FlushAsync(httpContext.RequestAborted);
                }

                await httpContext.Response.WriteAsync("data: [DONE]\n\n", httpContext.RequestAborted);
                await httpContext.Response.Body.FlushAsync(httpContext.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                // Client disconnected or request cancelled
            }
            catch (Exception ex)
            {
                var errorEvent = new AgentStepEvent("error", ex.Message);
                string errorJson = JsonSerializer.Serialize(errorEvent, AgentJsonOptions);
                await httpContext.Response.WriteAsync($"data: {errorJson}\n\n", httpContext.RequestAborted);
                await httpContext.Response.Body.FlushAsync(httpContext.RequestAborted);
            }

            return Results.Empty;
        });

        app.MapPost("/api/agent/approve-tool", (
            [FromBody] ApproveToolCallRequest request,
            AgentLoopEngine agentEngine) =>
        {
            if (request == null || string.IsNullOrWhiteSpace(request.CallId))
            {
                return CreateBadRequestProblem("Invalid CallId", "The callId parameter is required.", "agent_call_id_required");
            }

            bool success = agentEngine.ApproveToolCall(request.CallId, request.Approved);
            return Results.Ok(new { Success = success });
        });

        app.MapGet("/api/agent/runs/{runId}", async (
            string runId,
            IAgentRunStateRepository runStateRepository,
            CancellationToken cancellationToken) =>
        {
            AgentRunSnapshot? run = await runStateRepository.GetAsync(runId, cancellationToken);
            return run is null ? Results.NotFound() : Results.Ok(run);
        });

        app.MapGet("/api/agent/runs/resumable", async (
            IAgentRunStateRepository runStateRepository,
            CancellationToken cancellationToken) =>
        {
            IReadOnlyList<AgentRunSnapshot> runs = await runStateRepository.GetResumableAsync(cancellationToken);
            return Results.Ok(runs);
        });

        app.MapGet("/api/agent/runs/{runId}/trace", async (
            string runId,
            IAgentRunStateRepository runStateRepository,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await runStateRepository.ListTraceEventsAsync(runId, cancellationToken));
        });

        app.MapGet("/api/agent/runs/{runId}/evaluation", async (
            string runId,
            IAgentRunStateRepository runStateRepository,
            CancellationToken cancellationToken) =>
        {
            AgentRunSnapshot? run = await runStateRepository.GetAsync(runId, cancellationToken);
            if (run is null) return Results.NotFound();
            IReadOnlyList<AgentRunTraceEvent> trace = await runStateRepository.ListTraceEventsAsync(runId, cancellationToken);
            AgentRunEvaluationSummary summary = new(
                runId,
                run.Phase == AgentRunPhase.Completed,
                run.Phase is AgentRunPhase.Failed or AgentRunPhase.Cancelled,
                run.UpdatedAtUtc - run.StartedAtUtc,
                trace.Select(item => item.Step).DefaultIfEmpty(0).Max(),
                run.ToolCallsUsed,
                trace.Count(item => !string.IsNullOrWhiteSpace(item.Error) || item.Success is false),
                run.EstimatedTokensUsed,
                trace.Where(item => item.EventType == "model_response" && item.LatencyMs is not null).Sum(item => item.LatencyMs ?? 0),
                DateTimeOffset.UtcNow);
            return Results.Ok(summary);
        });

        // Multi-Agent Orchestration
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

        // MCP Integration
        RouteGroupBuilder mcpGroup = app.MapGroup("/api/mcp").WithTags("MCP");

        mcpGroup.MapGet("/servers", async (IMcpClientService mcpService, CancellationToken ct) =>
        {
            var servers = await mcpService.GetConfiguredServersAsync(ct);
            return Results.Ok(servers);
        });

        mcpGroup.MapPost("/servers", async (McpServerConfig config, IMcpClientService mcpService, CancellationToken ct) =>
        {
            var saved = await mcpService.RegisterServerAsync(config, ct);
            return Results.Ok(saved);
        });

        mcpGroup.MapDelete("/servers/{serverId}", async (string serverId, IMcpClientService mcpService, CancellationToken ct) =>
        {
            await mcpService.UnregisterServerAsync(serverId, ct);
            return Results.NoContent();
        });

        mcpGroup.MapGet("/tools", async (IMcpClientService mcpService, CancellationToken ct) =>
        {
            var tools = await mcpService.GetAvailableToolsAsync(ct);
            return Results.Ok(tools);
        });

        mcpGroup.MapPost("/tools/call", async (McpToolCallRequest request, IMcpClientService mcpService, CancellationToken ct) =>
        {
            var result = await mcpService.CallToolAsync(request, ct);
            return result.IsSuccess ? Results.Ok(result) : Results.BadRequest(result);
        });

        mcpGroup.MapGet("/servers/{serverId}/sse-status", (string serverId, IMcpSseClient sseClient) =>
        {
            var status = sseClient.GetSessionStatus(serverId);
            return Results.Ok(status);
        });
    }
}
