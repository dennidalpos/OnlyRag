namespace OnlyRag.Infrastructure.Images;

public sealed record ImageGenerationBinary(
    byte[] Content,
    string MimeType,
    string FileExtension);
