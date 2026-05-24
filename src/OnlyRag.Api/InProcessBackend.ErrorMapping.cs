using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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

    private static IResult MapOllamaException(
        OllamaApiException exception,
        IServiceProvider? services = null,
        string? operation = null)
    {
        if (exception.Kind == OllamaErrorKind.UnexpectedResponse && services is not null)
        {
            InProcessBackendDescriptor descriptor = services.GetRequiredService<InProcessBackendDescriptor>();
            string context = string.IsNullOrWhiteSpace(operation)
                ? "Ollama API unexpected response."
                : $"Ollama API unexpected response during {operation}.";
            BackendLog.WriteException(descriptor.StoragePaths, null, context, exception);
        }

        return exception.Kind switch
        {
            OllamaErrorKind.InvalidUrl => CreateProblem(
                "URL Ollama non valido",
                "L'indirizzo configurato per Ollama non e valido. Correggilo nelle impostazioni.",
                StatusCodes.Status400BadRequest,
                "ollama_invalid_url"),
            OllamaErrorKind.InvalidRequest => CreateProblem(
                "Richiesta Ollama non valida",
                "La richiesta inviata a Ollama non e valida. Controlla modello, parametri e impostazioni.",
                StatusCodes.Status400BadRequest,
                "ollama_invalid_request"),
            OllamaErrorKind.ContextLengthExceeded => CreateProblem(
                "Contesto Ollama troppo grande",
                "La richiesta supera la finestra di contesto del modello. Riduci i documenti selezionati o aumenta num_ctx.",
                StatusCodes.Status400BadRequest,
                "ollama_context_length_exceeded"),
            OllamaErrorKind.ModelNotFound => CreateProblem(
                "Modello assente",
                exception.Message,
                StatusCodes.Status404NotFound,
                "ollama_model_not_found"),
            OllamaErrorKind.Timeout => CreateProblem(
                "Timeout Ollama",
                "Ollama non ha risposto entro il tempo configurato. Controlla il servizio o aumenta il timeout.",
                StatusCodes.Status408RequestTimeout,
                "ollama_timeout"),
            OllamaErrorKind.Unreachable => CreateProblem(
                "Ollama non raggiungibile",
                "Ollama non e raggiungibile dall'app. Controlla che il servizio sia attivo e che l'endpoint sia corretto.",
                StatusCodes.Status503ServiceUnavailable,
                "ollama_unreachable"),
            _ => CreateProblem(
                "Errore Ollama",
                "Ollama ha restituito una risposta inattesa. I dettagli tecnici sono nei log locali.",
                StatusCodes.Status502BadGateway,
                "ollama_unexpected_response")
        };
    }

    private static IResult MapOfficeConversionException(OfficeConversionException _)
    {
        return CreateProblem(
            "Configurazione convertitore Office non valida",
            "La configurazione del convertitore Office non e valida. Controlla il percorso e i permessi nelle impostazioni.",
            StatusCodes.Status400BadRequest,
            "office_conversion_invalid_configuration");
    }

    private static IResult CreateUnexpectedErrorProblem(string title, string? correlationId = null)
    {
        return CreateProblem(
            title,
            CreateUnexpectedErrorDetail(correlationId),
            StatusCodes.Status500InternalServerError,
            "unexpected_error",
            correlationId);
    }

    private static IResult CreateBadRequestProblem(string title, string detail, string code = "bad_request")
    {
        return CreateProblem(title, detail, StatusCodes.Status400BadRequest, code);
    }

    private static IResult CreateConflictProblem(string title, string detail, string code = "conflict")
    {
        return CreateProblem(title, detail, StatusCodes.Status409Conflict, code);
    }

    private static IResult CreateNotFoundProblem(string resourceName = "Risorsa")
    {
        return CreateProblem(
            $"{resourceName} non trovato",
            "La risorsa richiesta non esiste o non e piu disponibile.",
            StatusCodes.Status404NotFound,
            "not_found");
    }

    private static IResult CreateProblem(
        string title,
        string detail,
        int statusCode,
        string code,
        string? correlationId = null)
    {
        Dictionary<string, object?> extensions = new()
        {
            ["code"] = code
        };
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            extensions["traceId"] = correlationId;
        }

        return Results.Problem(
            title: title,
            detail: detail,
            statusCode: statusCode,
            extensions: extensions);
    }

    private static Task WriteProblemAsync(
        HttpContext context,
        string title,
        string detail,
        int statusCode,
        string code,
        string? correlationId = null)
    {
        return CreateProblem(title, detail, statusCode, code, correlationId).ExecuteAsync(context);
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
