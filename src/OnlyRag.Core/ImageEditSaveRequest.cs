namespace OnlyRag.Core;

public sealed record ImageEditSaveRequest(
    string ImageBase64,
    string MimeType,
    int Width,
    int Height,
    bool ReplaceOriginal);
