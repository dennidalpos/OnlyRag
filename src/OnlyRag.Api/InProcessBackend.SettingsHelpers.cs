using Microsoft.AspNetCore.Http;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ingestion;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    private static async Task<PdfExportConverterStatusResponse> BuildPdfExportConverterStatusAsync(
        IPdfExportConverter converter,
        PdfExportSettingsStore settings,
        CancellationToken cancellationToken)
    {
        PdfExportSettings currentSettings = await settings.GetAsync(cancellationToken);
        PdfExportConverterAvailability availability = await converter.CheckAvailabilityAsync(cancellationToken);
        return CreatePdfExportConverterStatusResponse(availability, currentSettings.ConversionTimeoutSeconds);
    }

    private static async Task<IResult> DeleteOllamaModelAsync(
        string name,
        IOllamaClient ollamaClient,
        IOllamaSettingsService settings,
        IServiceProvider services,
        string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            string modelName = OllamaSettingsService.NormalizeRequiredModelName(name);
            await ollamaClient.DeleteModelAsync(modelName, cancellationToken);
            await settings.ClearMissingDefaultModelAsync(modelName, cancellationToken);
            return Results.Ok(new OperationMessageResponse($"Modello {modelName} rimosso."));
        }
        catch (OllamaApiException ex)
        {
            return MapOllamaException(ex, services, operation);
        }
    }

    private static async Task<IResult> ShowOllamaModelDetailsAsync(
        string name,
        IOllamaClient ollamaClient,
        IServiceProvider services,
        string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            OllamaModelDetails details = await ollamaClient.ShowModelAsync(name, cancellationToken);
            return Results.Ok(details);
        }
        catch (OllamaApiException ex)
        {
            return MapOllamaException(ex, services, operation);
        }
    }
}
