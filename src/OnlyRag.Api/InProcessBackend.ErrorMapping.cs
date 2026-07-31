using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Images;
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
                $"The {usage} model '{model}' is not installed in Ollama.");
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
                "Invalid Ollama URL",
                "The configured Ollama address is invalid. Please correct it in Settings.",
                StatusCodes.Status400BadRequest,
                "ollama_invalid_url"),
            OllamaErrorKind.InvalidRequest => CreateProblem(
                "Invalid Ollama request",
                "The request sent to Ollama is invalid. Check model, parameters, and settings.",
                StatusCodes.Status400BadRequest,
                "ollama_invalid_request"),
            OllamaErrorKind.ContextLengthExceeded => CreateProblem(
                "Ollama context too large",
                "The request exceeds the model's context window. Reduce selected documents or increase num_ctx.",
                StatusCodes.Status400BadRequest,
                "ollama_context_length_exceeded"),
            OllamaErrorKind.ModelNotFound => CreateProblem(
                "Model not found",
                exception.Message,
                StatusCodes.Status404NotFound,
                "ollama_model_not_found"),
            OllamaErrorKind.Timeout => CreateProblem(
                "Ollama timeout",
                "Ollama did not respond within the configured timeout. Check the service or increase the timeout.",
                StatusCodes.Status408RequestTimeout,
                "ollama_timeout"),
            OllamaErrorKind.Unreachable => CreateProblem(
                "Ollama unreachable",
                "Ollama is not reachable from the app. Check that the service is running and the endpoint is correct.",
                StatusCodes.Status503ServiceUnavailable,
                "ollama_unreachable"),
            _ => CreateProblem(
                "Ollama error",
                "Ollama returned an unexpected response. Technical details are in the local logs.",
                StatusCodes.Status502BadGateway,
                "ollama_unexpected_response")
        };
    }

    private static IResult MapImageGenerationException(
        ImageGenerationException exception,
        IServiceProvider? services = null,
        string? operation = null)
    {
        if (exception.Kind == ImageGenerationErrorKind.UnexpectedResponse && services is not null)
        {
            InProcessBackendDescriptor descriptor = services.GetRequiredService<InProcessBackendDescriptor>();
            string context = string.IsNullOrWhiteSpace(operation)
                ? "Image generation unexpected response."
                : $"Image generation unexpected response during {operation}.";
            BackendLog.WriteException(descriptor.StoragePaths, null, context, exception);
        }

        return exception.Kind switch
        {
            ImageGenerationErrorKind.InvalidConfiguration => CreateProblem(
                "Invalid image configuration",
                exception.Message,
                StatusCodes.Status400BadRequest,
                "image_generation_invalid_configuration"),
            ImageGenerationErrorKind.InvalidRequest => CreateProblem(
                "Invalid image request",
                exception.Message,
                StatusCodes.Status400BadRequest,
                "image_generation_invalid_request"),
            ImageGenerationErrorKind.Timeout => CreateProblem(
                "Image generation timeout",
                exception.Message,
                StatusCodes.Status408RequestTimeout,
                "image_generation_timeout"),
            ImageGenerationErrorKind.ModelNotReady => CreateProblem(
                "Image model not ready",
                exception.Message,
                StatusCodes.Status409Conflict,
                "image_generation_model_not_ready"),
            ImageGenerationErrorKind.Unreachable => CreateProblem(
                "Image model download unreachable",
                exception.Message,
                StatusCodes.Status503ServiceUnavailable,
                "image_generation_unreachable"),
            ImageGenerationErrorKind.NotFound => CreateProblem(
                "Image resource not found",
                exception.Message,
                StatusCodes.Status404NotFound,
                "image_generation_not_found"),
            _ => CreateProblem(
                "Image generation error",
                "The integrated image generator returned an unexpected response. Technical details are in the local logs.",
                StatusCodes.Status502BadGateway,
                "image_generation_unexpected_response")
        };
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

    private static IResult CreateNotFoundProblem(string resourceName = "Resource")
    {
        return CreateProblem(
            $"{resourceName} not found",
            "The requested resource does not exist or is no longer available.",
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
            ? "An unexpected error occurred. Details have been recorded in the local logs."
            : $"An unexpected error occurred. Details have been recorded in the local logs with reference {correlationId}.";
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
                "Open Settings and correct the Ollama address."),
            OllamaErrorKind.Timeout => new OllamaStatusResponse(
                "Offline",
                false,
                baseUrl,
                0,
                "Ollama did not respond in time.",
                "Check that Ollama is running and increase the timeout if the machine is slow."),
            OllamaErrorKind.Unreachable => new OllamaStatusResponse(
                "Offline",
                false,
                baseUrl,
                0,
                "Ollama is unreachable.",
                "Verify that Ollama is open or that the LAN host is correct and accessible."),
            _ => new OllamaStatusResponse(
                "Offline",
                false,
                baseUrl,
                0,
                exception.Message,
                "Check configuration and models in Settings.")
        };
    }
}
