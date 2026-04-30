using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Api.Ollama;
using OnlyRag.Infrastructure.Ocr;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    private static void MapDependencyEndpoints(WebApplication app)
    {
        app.MapGet("/api/dependencies/ollama", async (
            DependencyProvisioningService dependencies,
            IOllamaClient ollamaClient,
            CancellationToken cancellationToken) =>
        {
            bool apiReachable;
            try
            {
                await ollamaClient.ListModelsAsync(cancellationToken);
                apiReachable = true;
            }
            catch (OllamaApiException)
            {
                apiReachable = false;
            }

            return Results.Ok(dependencies.CreateOllamaStatus(apiReachable));
        });

        app.MapPost("/api/dependencies/ollama/install", (DependencyProvisioningService dependencies) =>
        {
            try
            {
                return Results.Ok(dependencies.StartOllamaInstall());
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(
                    title: "Installazione Ollama non avviata",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status400BadRequest);
            }
        });

        app.MapPost("/api/dependencies/libreoffice/open-download", (DependencyProvisioningService dependencies) =>
        {
            try
            {
                return Results.Ok(dependencies.OpenLibreOfficeDownload());
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(
                    title: "Download LibreOffice non aperto",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        app.MapGet("/api/dependencies/ocr", async (
            DependencyProvisioningService dependencies,
            IOcrEngine ocrEngine,
            CancellationToken cancellationToken) =>
            Results.Ok(await dependencies.GetOcrStatusAsync(ocrEngine, cancellationToken)));

        app.MapPost("/api/dependencies/ocr/provision", (DependencyProvisioningService dependencies) =>
            Results.Ok(dependencies.StartOcrProvision()));
    }
}
