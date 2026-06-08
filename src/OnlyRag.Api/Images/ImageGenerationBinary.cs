namespace OnlyRag.Api.Images;

internal sealed record ImageGenerationBinary(
    byte[] Content,
    string MimeType,
    string FileExtension);

