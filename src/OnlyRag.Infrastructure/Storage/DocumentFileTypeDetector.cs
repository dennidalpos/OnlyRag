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
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".gif" => "image/gif",
            ".jpeg" or ".jpg" => "image/jpeg",
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".rtf" => "application/rtf",
            ".tif" or ".tiff" => "image/tiff",
            ".txt" => "text/plain",
            ".webp" => "image/webp",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream"
        };
    }
}
