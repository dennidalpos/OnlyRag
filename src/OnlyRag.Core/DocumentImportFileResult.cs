namespace OnlyRag.Core;

public sealed record DocumentImportFileResult(
    string FileName,
    ImportedDocument? Document,
    bool Deduplicated,
    bool Succeeded,
    string Message,
    string? ErrorCode)
{
    public static DocumentImportFileResult Imported(string fileName, DocumentImportResult result)
    {
        return new DocumentImportFileResult(
            fileName,
            result.Document,
            result.Deduplicated,
            Succeeded: true,
            result.Message,
            ErrorCode: null);
    }

    public static DocumentImportFileResult Failed(string fileName, string message, string errorCode)
    {
        return new DocumentImportFileResult(
            fileName,
            Document: null,
            Deduplicated: false,
            Succeeded: false,
            message,
            errorCode);
    }
}
