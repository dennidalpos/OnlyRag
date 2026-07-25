using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Core;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    internal static void MapCodingEndpoints(this WebApplication app)
    {
        app.MapPost("/api/coding/generate", async (
            CodingTaskRequest request,
            CodingService codingService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Prompt))
            {
                return Results.BadRequest(new { error = "Il campo Prompt è obbligatorio." });
            }

            try
            {
                CodingTaskResponse response = await codingService.GenerateCodeAsync(request, cancellationToken);
                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    title: "Errore durante la generazione del codice");
            }
        });

        app.MapPost("/api/coding/generate-stream", async (
            CodingTaskRequest request,
            CodingService codingService,
            HttpResponse response,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Prompt))
            {
                response.StatusCode = StatusCodes.Status400BadRequest;
                await response.WriteAsync("Il campo Prompt è obbligatorio.", cancellationToken);
                return;
            }

            response.ContentType = "text/event-stream";
            response.Headers.Append("Cache-Control", "no-cache");
            response.Headers.Append("Connection", "keep-alive");

            try
            {
                await foreach (string chunk in codingService.GenerateCodeStreamAsync(request, cancellationToken))
                {
                    string data = System.Text.Json.JsonSerializer.Serialize(new { chunk });
                    await response.WriteAsync($"data: {data}\n\n", cancellationToken);
                    await response.Body.FlushAsync(cancellationToken);
                }
                await response.WriteAsync("data: [DONE]\n\n", cancellationToken);
                await response.Body.FlushAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                string errData = System.Text.Json.JsonSerializer.Serialize(new { error = ex.Message });
                await response.WriteAsync($"data: {errData}\n\n", cancellationToken);
                await response.Body.FlushAsync(cancellationToken);
            }
        });

        app.MapPost("/api/coding/refactor", async (
            CodeRefactorRequest request,
            CodingService codingService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.OriginalCode))
            {
                return Results.BadRequest(new { error = "Il campo OriginalCode è obbligatorio." });
            }

            try
            {
                CodeRefactorResponse response = await codingService.RefactorCodeAsync(request, cancellationToken);
                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    title: "Errore durante il refactoring del codice");
            }
        });

        app.MapPost("/api/coding/diagnose", async (
            CodeDiagnoseRequest request,
            CodingService codingService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.ErrorLog))
            {
                return Results.BadRequest(new { error = "Il campo ErrorLog è obbligatorio." });
            }

            try
            {
                CodeDiagnoseResponse response = await codingService.DiagnoseCodeAsync(request, cancellationToken);
                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    title: "Errore durante la diagnosi dell'errore");
            }
        });

        app.MapPost("/api/agent/run-stream", async (
            AgentRunRequest request,
            AgentLoopEngine agentEngine,
            WorkspaceService workspaceService,
            OnlyRag.Infrastructure.Logging.ILoggingService loggingService,
            HttpResponse response,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Goal))
            {
                response.StatusCode = StatusCodes.Status400BadRequest;
                await response.WriteAsync("Il campo Goal è obbligatorio.", cancellationToken);
                return;
            }

            string? rootPath = request.WorkspaceRoot;
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                var config = await workspaceService.GetConfigAsync(cancellationToken);
                rootPath = config.RootPath;
            }

            var runReq = request with { WorkspaceRoot = rootPath };

            response.ContentType = "text/event-stream";
            response.Headers.Append("Cache-Control", "no-cache");
            response.Headers.Append("Connection", "keep-alive");

            try
            {
                await foreach (AgentStepEvent stepEvent in agentEngine.RunAgentLoopAsync(runReq, cancellationToken))
                {
                    string data = System.Text.Json.JsonSerializer.Serialize(stepEvent);
                    await response.WriteAsync($"data: {data}\n\n", cancellationToken);
                    await response.Body.FlushAsync(cancellationToken);
                }
                await response.WriteAsync("data: [DONE]\n\n", cancellationToken);
                await response.Body.FlushAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                loggingService.LogError("AgentEngine", $"Eccezione non gestita durante l'esecuzione dell'agente: {ex.Message}", ex);
                string errData = System.Text.Json.JsonSerializer.Serialize(new AgentStepEvent("error", ex.Message));
                await response.WriteAsync($"data: {errData}\n\n", cancellationToken);
                await response.Body.FlushAsync(cancellationToken);
            }
        });

        app.MapPost("/api/agent/approve-tool", (
            ApproveToolCallRequest request,
            AgentLoopEngine agentEngine) =>
        {
            bool success = agentEngine.ApproveToolCall(request.CallId, request.Approved);
            return Results.Ok(new { success });
        });

        app.MapGet("/api/agent/tasks", (OnlyRag.Infrastructure.Agent.BackgroundTaskManager taskManager) =>
        {
            var tasks = taskManager.ListTasks();
            return Results.Ok(tasks);
        });

        app.MapPost("/api/agent/tasks/manage", (
            ManageTaskRequest request,
            OnlyRag.Infrastructure.Agent.BackgroundTaskManager taskManager) =>
        {
            if (string.Equals(request.Action, "kill", StringComparison.OrdinalIgnoreCase))
            {
                bool killed = taskManager.KillTask(request.TaskId);
                return Results.Ok(new { success = killed });
            }
            else if (string.Equals(request.Action, "send_input", StringComparison.OrdinalIgnoreCase))
            {
                bool sent = taskManager.SendInput(request.TaskId, request.Input ?? "");
                return Results.Ok(new { success = sent });
            }
            else if (string.Equals(request.Action, "status", StringComparison.OrdinalIgnoreCase))
            {
                var status = taskManager.GetTaskStatusAndLogs(request.TaskId);
                if (status.HasValue)
                {
                    return Results.Ok(new { info = status.Value.Info, logs = status.Value.Logs });
                }
                return Results.NotFound(new { error = "Task non trovato" });
            }

            return Results.BadRequest(new { error = "Azione non valida. Utilizzare kill, send_input o status." });
        });
    }
}
