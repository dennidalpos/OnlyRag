using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OnlyRag.Core;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    private static readonly JsonSerializerOptions AgentJsonOptions = new(JsonSerializerDefaults.Web);

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
    }
}
