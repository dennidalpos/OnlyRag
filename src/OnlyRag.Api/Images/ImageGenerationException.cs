namespace OnlyRag.Api.Images;

internal enum ImageGenerationErrorKind
{
    InvalidConfiguration,
    InvalidRequest,
    Timeout,
    Unreachable,
    UnexpectedResponse,
    NotFound
}

internal sealed class ImageGenerationException : Exception
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

