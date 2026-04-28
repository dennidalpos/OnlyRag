namespace OnlyRag.Api.Ollama;

internal sealed class OllamaApiException : Exception
{
    public OllamaApiException(
        OllamaErrorKind kind,
        string message,
        int? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        StatusCode = statusCode;
    }

    public OllamaErrorKind Kind { get; }

    public int? StatusCode { get; }
}
