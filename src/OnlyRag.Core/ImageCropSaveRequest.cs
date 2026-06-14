namespace OnlyRag.Core;

public sealed record ImageCropSaveRequest(
    string ImageBase64,
    string MimeType,
    int Width,
    int Height,
    bool ReplaceOriginal);
