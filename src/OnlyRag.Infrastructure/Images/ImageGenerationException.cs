namespace OnlyRag.Infrastructure.Images;

public enum ImageGenerationErrorKind
{
    InvalidConfiguration,
    InvalidRequest,
    Timeout,
    Unreachable,
    ModelNotReady,
    UnexpectedResponse,
    NotFound
}

public sealed class ImageGenerationException : Exception
{
    public ImageGenerationException(
        ImageGenerationErrorKind kind,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public ImageGenerationErrorKind Kind { get; }
}
