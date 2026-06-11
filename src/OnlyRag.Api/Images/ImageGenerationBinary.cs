namespace OnlyRag.Api.Images;

public sealed record ImageGenerationBinary(
    byte[] Content,
    string MimeType,
    string FileExtension);
