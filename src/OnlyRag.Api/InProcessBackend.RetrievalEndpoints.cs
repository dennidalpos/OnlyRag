using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Retrieval;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    private static void MapRetrievalEndpoints(WebApplication app)
    {
        app.MapPost("/api/search", async (
            DocumentSearchRequest request,
            IHybridRetrievalService retrieval,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return Results.Problem(
                    title: "Query non valida",
                    detail: "Inserisci una query di ricerca.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (request.DocumentIds is null || request.DocumentIds.Count == 0)
            {
                return Results.Problem(
                    title: "Documenti non selezionati",
                    detail: "Seleziona almeno un documento prima di cercare.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Ok(await retrieval.SearchAsync(request, cancellationToken));
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
                return Results.Problem(
                    title: ex.Title,
                    detail: ex.Message,
                    statusCode: StatusCodes.Status400BadRequest);
            }
            catch (OllamaApiException ex)
            {
                return MapOllamaException(ex);
            }
        });
    }
}