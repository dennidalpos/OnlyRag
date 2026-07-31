using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ocr;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    internal static void MapDependencyEndpoints(this WebApplication app)
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
                return CreateBadRequestProblem(
                    "Confirmation required",
                    "Starting Ollama installation requires explicit UI confirmation.",
                    "confirmation_required");
            }

            try
            {
                return Results.Ok(dependencies.StartOllamaInstall());
            }
            catch (InvalidOperationException ex)
            {
                return CreateBadRequestProblem(
                    "Ollama installation not started",
                    ex.Message,
                    "dependency_install_not_started");
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
                return CreateBadRequestProblem(
                    "Confirmation required",
                    "Opening LibreOffice download for PDF export requires explicit UI confirmation.",
                    "confirmation_required");
            }

            try
            {
                return Results.Ok(dependencies.OpenLibreOfficeDownload());
            }
            catch (InvalidOperationException ex)
            {
                BackendLog.WriteException(descriptor.StoragePaths, httpContext.TraceIdentifier, "Open LibreOffice PDF export download failed.", ex);
                return CreateUnexpectedErrorProblem("LibreOffice download not opened", httpContext.TraceIdentifier);
            }
        });

        app.MapGet("/api/dependencies/ocr", async (
            DependencyProvisioningService dependencies,
            IOcrEngine ocrEngine,
            OcrGpuCapabilityService gpuCapability,
            CancellationToken cancellationToken) =>
            Results.Ok(await dependencies.GetOcrStatusAsync(ocrEngine, gpuCapability, cancellationToken)));

        app.MapGet("/api/dependencies/ocr/startup-analysis", async (
            OcrStartupAnalysisService startupAnalysis,
            IOcrEngine ocrEngine,
            OcrGpuCapabilityService gpuCapability,
            CancellationToken cancellationToken) =>
            Results.Ok(await startupAnalysis.AnalyzeAsync(ocrEngine, gpuCapability, cancellationToken)));

        app.MapPost("/api/dependencies/ocr/provision", (
            HttpContext httpContext,
            OcrProvisionRequest request,
            InProcessBackendDescriptor descriptor,
            DependencyProvisioningService dependencies) =>
        {
            if (!request.Confirmed)
            {
                return CreateBadRequestProblem(
                    "Confirmation required",
                    "OCR configuration requires explicit UI confirmation.",
                    "confirmation_required");
            }

            try
            {
                return Results.Ok(dependencies.StartOcrProvision(request.RuntimeTarget));
            }
            catch (InvalidOperationException ex)
            {
                BackendLog.WriteException(descriptor.StoragePaths, httpContext.TraceIdentifier, "Start OCR provisioning failed.", ex);
                return CreateUnexpectedErrorProblem("OCR configuration not started", httpContext.TraceIdentifier);
            }
        });

        app.MapPost("/api/dependencies/ocr/cancel", (
            ProcessLaunchRequest request,
            DependencyProvisioningService dependencies) =>
        {
            if (!request.Confirmed)
            {
                return CreateBadRequestProblem(
                    "Confirmation required",
                    "Cancelling OCR configuration requires explicit UI confirmation.",
                    "confirmation_required");
            }

            return Results.Ok(dependencies.CancelOcrProvision());
        });
    }
}
