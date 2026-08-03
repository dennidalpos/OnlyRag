namespace OnlyRag.Core;

public enum ExportReportFormat
{
    Pdf = 0,
    Docx = 1
}

public sealed record ExportCitationItem(
    string DocumentName,
    int? PageStart,
    int? PageEnd,
    long ChunkId,
    string Snippet);

public sealed record ExportMessageItem(
    string Role,
    string Text,
    IReadOnlyList<ExportCitationItem>? Citations = null);

public sealed record ExportChatReportRequest(
    string Title,
    ExportReportFormat Format,
    IReadOnlyList<ExportMessageItem> Messages,
    bool IncludeCitations = true,
    string? Notes = null);

public sealed record ExportReportResult(
    string FilePath,
    string FileName,
    long FileSizeBytes,
    string Format);

public sealed record ExportPreviewRequest(
    string Title,
    ExportReportFormat Format,
    IReadOnlyList<ExportMessageItem> Messages,
    bool IncludeCitations = true,
    string? Notes = null,
    string Theme = "Modern");

public sealed record ExportPreviewResponse(
    string HtmlPreview,
    int EstimatedPageCount,
    int TotalMessageCount,
    int TotalCitationCount,
    long EstimatedFileSizeBytes,
    string Theme);
