using Microsoft.AspNetCore.Http;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ingestion;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    private static async Task EnsureOllamaModelInstalledAsync(
        IOllamaClient ollamaClient,
        string model,
        string usage,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<OllamaModelSummary> models = await ollamaClient.ListModelsAsync(cancellationToken);
        bool installed = models.Any(installedModel =>
            string.Equals(installedModel.Name, model, StringComparison.OrdinalIgnoreCase)
            || string.Equals(installedModel.Model, model, StringComparison.OrdinalIgnoreCase));
        if (!installed)
        {
            throw new OllamaApiException(
                OllamaErrorKind.ModelNotFound,
                $"Il modello {usage} '{model}' non e installato in Ollama.");
        }
    }

    private static IResult MapOllamaException(OllamaApiException exception)
    {
        return exception.Kind switch
        {
            OllamaErrorKind.InvalidUrl => Results.Problem(
                title: "URL Ollama non valido",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest),
            OllamaErrorKind.InvalidRequest => Results.Problem(
                title: "Richiesta Ollama non valida",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest),
            OllamaErrorKind.ModelNotFound => Results.Problem(
                title: "Modello assente",
                detail: exception.Message,
                statusCode: StatusCodes.Status404NotFound),
            OllamaErrorKind.Timeout => Results.Problem(
                title: "Timeout Ollama",
                detail: exception.Message,
                statusCode: StatusCodes.Status408RequestTimeout),
            OllamaErrorKind.Unreachable => Results.Problem(
                title: "Ollama non raggiungibile",
                detail: exception.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Results.Problem(
                title: "Errore Ollama",
                detail: exception.Message,
                statusCode: StatusCodes.Status502BadGateway)
        };
    }

    private static IResult MapOfficeConversionException(OfficeConversionException exception)
    {
        return Results.Problem(
            title: "Configurazione convertitore Office non valida",
            detail: exception.Message,
            statusCode: StatusCodes.Status400BadRequest);
    }

    private static IResult CreateUnexpectedErrorProblem(string title, string? correlationId = null)
    {
        return Results.Problem(
            title: title,
            detail: CreateUnexpectedErrorDetail(correlationId),
            statusCode: StatusCodes.Status500InternalServerError);
    }

    private static string CreateUnexpectedErrorDetail(string? correlationId = null)
    {
        return string.IsNullOrWhiteSpace(correlationId)
            ? "Si e verificato un errore imprevisto. I dettagli sono stati registrati nei log locali."
            : $"Si e verificato un errore imprevisto. I dettagli sono stati registrati nei log locali con riferimento {correlationId}.";
    }

    private static OfficeConverterStatusResponse CreateOfficeConverterStatusResponse(
        OfficeConverterAvailability availability,
        int timeoutSeconds)
    {
        return new OfficeConverterStatusResponse(
            availability.IsAvailable ? "Available" : "RequiresAdditionalComponent",
            availability.IsAvailable,
            availability.ExecutablePath,
            availability.Message,
            availability.Suggestion,
            timeoutSeconds);
    }

    private static OllamaStatusResponse CreateStatusResponse(string baseUrl, OllamaApiException exception)
    {
        return exception.Kind switch
        {
            OllamaErrorKind.InvalidUrl => new OllamaStatusResponse(
                "Offline",
                false,
                baseUrl,
                0,
                exception.Message,
                "Apri Impostazioni e correggi l'indirizzo Ollama."),
            OllamaErrorKind.Timeout => new OllamaStatusResponse(
                "Offline",
                false,
                baseUrl,
                0,
                "Ollama non ha risposto in tempo.",
                "Controlla che Ollama sia attivo e aumenta il timeout se la macchina e lenta."),
            OllamaErrorKind.Unreachable => new OllamaStatusResponse(
                "Offline",
                false,
                baseUrl,
                0,
                "Ollama non e raggiungibile.",
                "Verifica che Ollama sia aperto oppure che l'host LAN sia corretto e accessibile."),
            _ => new OllamaStatusResponse(
                "Offline",
                false,
                baseUrl,
                0,
                exception.Message,
                "Controlla configurazione e modelli in Impostazioni.")
        };
    }
}
