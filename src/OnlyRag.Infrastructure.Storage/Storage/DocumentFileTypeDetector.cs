namespace OnlyRag.Infrastructure.Storage;

public static class DocumentFileTypeDetector
{
    public static string DetectMimeType(string fileName)
    {
        string extension = SafeDocumentPath.NormalizeFileExtension(fileName);
        return extension switch
        {
            ".bmp" => "image/bmp",
            ".csv" => "text/csv",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".gif" => "image/gif",
            ".jpeg" or ".jpg" => "image/jpeg",
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".rtf" => "application/rtf",
            ".tif" or ".tiff" => "image/tiff",
            ".txt" => "text/plain",
            ".webp" => "image/webp",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".7z" => "application/x-7z-compressed",
            ".tar" => "application/x-tar",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };
    }
}
