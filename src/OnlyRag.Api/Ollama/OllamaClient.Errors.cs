using System.Net;
using System.Text;
using System.Text.Json;

namespace OnlyRag.Api.Ollama;

internal sealed partial class OllamaClient
{
    private const int MaxErrorResponseCharacters = 4096;

    private static OllamaApiException CreateApiException(HttpStatusCode statusCode, string errorMessage)
    {
        if (statusCode == HttpStatusCode.NotFound || errorMessage.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return new OllamaApiException(
                OllamaErrorKind.ModelNotFound,
                "Il modello richiesto non e presente in Ollama.",
                (int)statusCode);
        }

        if (statusCode == HttpStatusCode.BadRequest)
        {
            if (errorMessage.Contains("input length", StringComparison.OrdinalIgnoreCase)
                && errorMessage.Contains("context length", StringComparison.OrdinalIgnoreCase))
            {
                return new OllamaApiException(
                    OllamaErrorKind.ContextLengthExceeded,
                    "La richiesta supera la finestra di contesto del modello Ollama.",
                    (int)statusCode);
            }

            return new OllamaApiException(
                OllamaErrorKind.InvalidRequest,
                "La richiesta verso Ollama non e valida.",
                (int)statusCode);
        }

        return new OllamaApiException(
            OllamaErrorKind.UnexpectedResponse,
            $"Ollama ha restituito lo stato HTTP {(int)statusCode}.",
            (int)statusCode);
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using StreamReader reader = new(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024,
            leaveOpen: false);
        char[] buffer = GC.AllocateUninitializedArray<char>(MaxErrorResponseCharacters + 1);
        int read = await reader.ReadBlockAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
        bool truncated = read > MaxErrorResponseCharacters;
        string content = NormalizeExternalErrorText(new string(buffer, 0, Math.Min(read, MaxErrorResponseCharacters)));
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        try
        {
            OllamaErrorResponse? error = JsonSerializer.Deserialize<OllamaErrorResponse>(content, JsonOptions);
            if (!string.IsNullOrWhiteSpace(error?.Error))
            {
                return NormalizeExternalErrorText(error.Error);
            }
        }
        catch (JsonException)
        {
        }

        return truncated
            ? $"{content.Trim()} [risposta troncata]"
            : content.Trim();
    }

    private static string NormalizeExternalErrorText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        StringBuilder builder = new(text.Length);
        foreach (char character in text)
        {
            builder.Append(char.IsControl(character) && character is not ('\r' or '\n' or '\t')
                ? ' '
                : character);
        }

        return builder.ToString().Trim();
    }
}
