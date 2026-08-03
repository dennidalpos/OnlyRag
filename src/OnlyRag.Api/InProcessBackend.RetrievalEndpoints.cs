using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Retrieval;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    internal static void MapRetrievalEndpoints(this WebApplication app)
    {
        app.MapPost("/api/search", async (
            DocumentSearchRequest request,
            IHybridRetrievalService retrieval,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return CreateBadRequestProblem(
                    "Invalid query",
                    "Enter a search query.",
                    "search_query_required");
            }

            if (request.DocumentIds is null || request.DocumentIds.Count == 0)
            {
                return CreateBadRequestProblem(
                    "No documents selected",
                    "Select at least one document before searching.",
                    "documents_required");
            }

            try
            {
                return Results.Ok(await retrieval.SearchAsync(request, cancellationToken));
            }
            catch (InvalidOperationException ex)
            {
                return CreateProblem(
                    "Qdrant retrieval unavailable",
                    UserFacingErrorText.FromExternalDetail(ex.Message, "Rebuild embeddings or verify Qdrant connection."),
                    StatusCodes.Status503ServiceUnavailable,
                    "qdrant_retrieval_unavailable");
            }
        });

        app.MapPost("/api/chat", async (
            ChatRequest request,
            ChatService chatService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await chatService.SendAsync(request, cancellationToken));
            }
            catch (ChatValidationException ex)
            {
                return CreateBadRequestProblem(ex.Title, ex.Message, "chat_validation_failed");
            }
            catch (InvalidOperationException ex)
            {
                return CreateProblem(
                    "Document chat unavailable",
                    UserFacingErrorText.FromExternalDetail(ex.Message, "Rebuild embeddings or verify Qdrant connection."),
                    StatusCodes.Status503ServiceUnavailable,
                    "qdrant_retrieval_unavailable");
            }
            catch (OllamaApiException ex)
            {
                return MapOllamaException(ex, app.Services, "/api/chat");
            }
        });

        app.MapPost("/api/chat/stream", async (
            HttpContext httpContext,
            ChatRequest request,
            ChatService chatService) =>
        {
            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Connection = "keep-alive";

            try
            {
                await foreach (ChatStreamChunkEvent evt in chatService.SendStreamAsync(request, httpContext.RequestAborted))
                {
                    string json = System.Text.Json.JsonSerializer.Serialize(evt, AgentJsonOptions);
                    await httpContext.Response.WriteAsync($"data: {json}\n\n", httpContext.RequestAborted);
                    await httpContext.Response.Body.FlushAsync(httpContext.RequestAborted);
                }

                await httpContext.Response.WriteAsync("data: [DONE]\n\n", httpContext.RequestAborted);
                await httpContext.Response.Body.FlushAsync(httpContext.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                // Client cancelled or disconnected
            }
            catch (ChatValidationException ex)
            {
                var errorEvt = new ChatStreamChunkEvent("error", Content: ex.Message);
                string json = System.Text.Json.JsonSerializer.Serialize(errorEvt, AgentJsonOptions);
                await httpContext.Response.WriteAsync($"data: {json}\n\n", httpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                var errorEvt = new ChatStreamChunkEvent("error", Content: ex.Message);
                string json = System.Text.Json.JsonSerializer.Serialize(errorEvt, AgentJsonOptions);
                await httpContext.Response.WriteAsync($"data: {json}\n\n", httpContext.RequestAborted);
            }

            return Results.Empty;
        });

        app.MapPost("/api/vector/repair", async (IQdrantSyncRepairService repairService, CancellationToken cancellationToken) =>
        {
            QdrantSyncReport report = await repairService.AuditAndRepairAsync(cancellationToken);
            return Results.Ok(report);
        });
    }
}
