using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
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

        app.MapPost("/api/dependencies/ollama/install", (
            ProcessLaunchRequest request,
            DependencyProvisioningService dependencies) =>
        {
            if (!request.Confirmed)
            {
                return Results.Problem(
                    title: "Conferma richiesta",
                    detail: "L'avvio dell'installazione Ollama richiede una conferma esplicita dalla UI.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

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

        app.MapPost("/api/dependencies/libreoffice/open-download", (
            HttpContext httpContext,
            ProcessLaunchRequest request,
            InProcessBackendDescriptor descriptor,
            DependencyProvisioningService dependencies) =>
        {
            if (!request.Confirmed)
            {
                return Results.Problem(
                    title: "Conferma richiesta",
                    detail: "L'apertura del download LibreOffice richiede una conferma esplicita dalla UI.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                return Results.Ok(dependencies.OpenLibreOfficeDownload());
            }
            catch (InvalidOperationException ex)
            {
                BackendLog.WriteException(descriptor.StoragePaths, httpContext.TraceIdentifier, "Open LibreOffice download failed.", ex);
                return CreateUnexpectedErrorProblem("Download LibreOffice non aperto", httpContext.TraceIdentifier);
            }
        });

        app.MapGet("/api/dependencies/ocr", async (
            DependencyProvisioningService dependencies,
            IOcrEngine ocrEngine,
            CancellationToken cancellationToken) =>
            Results.Ok(await dependencies.GetOcrStatusAsync(ocrEngine, cancellationToken)));

        app.MapPost("/api/dependencies/ocr/provision", (
            HttpContext httpContext,
            ProcessLaunchRequest request,
            InProcessBackendDescriptor descriptor,
            DependencyProvisioningService dependencies) =>
        {
            if (!request.Confirmed)
            {
                return Results.Problem(
                    title: "Conferma richiesta",
                    detail: "La configurazione OCR richiede una conferma esplicita dalla UI.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                return Results.Ok(dependencies.StartOcrProvision());
            }
            catch (InvalidOperationException ex)
            {
                BackendLog.WriteException(descriptor.StoragePaths, httpContext.TraceIdentifier, "Start OCR provisioning failed.", ex);
                return CreateUnexpectedErrorProblem("Configurazione OCR non avviata", httpContext.TraceIdentifier);
            }
        });
    }
}
