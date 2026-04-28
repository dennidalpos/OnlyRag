namespace OnlyRag.Api.Ollama;

internal enum OllamaErrorKind
{
    InvalidUrl,
    Unreachable,
    Timeout,
    ModelNotFound,
    InvalidRequest,
    ContextLengthExceeded,
    UnexpectedResponse
}
