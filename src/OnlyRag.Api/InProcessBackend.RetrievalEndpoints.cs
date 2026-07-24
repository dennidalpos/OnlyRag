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
                    "Query non valida",
                    "Inserisci una query di ricerca.",
                    "search_query_required");
            }

            if (request.DocumentIds is null || request.DocumentIds.Count == 0)
            {
                return CreateBadRequestProblem(
                    "Documenti non selezionati",
                    "Seleziona almeno un documento prima di cercare.",
                    "documents_required");
            }

            try
            {
                return Results.Ok(await retrieval.SearchAsync(request, cancellationToken));
            }
            catch (InvalidOperationException ex)
            {
                return CreateProblem(
                    "Retrieval Qdrant non disponibile",
                    UserFacingErrorText.FromExternalDetail(ex.Message, "Ricostruisci gli embedding o verifica la connessione Qdrant."),
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
                    "Chat documentale non disponibile",
                    UserFacingErrorText.FromExternalDetail(ex.Message, "Ricostruisci gli embedding o verifica la connessione Qdrant."),
                    StatusCodes.Status503ServiceUnavailable,
                    "qdrant_retrieval_unavailable");
            }
            catch (OllamaApiException ex)
            {
                return MapOllamaException(ex, app.Services, "/api/chat");
            }
        });
    }
}
