using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Core;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    internal static void MapCodingEndpoints(WebApplication app)
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
    }
}
