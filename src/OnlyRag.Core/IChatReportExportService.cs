namespace OnlyRag.Core;

public interface IChatReportExportService
{
    Task<ExportPreviewResponse> GeneratePreviewAsync(
        ExportPreviewRequest request,
        CancellationToken cancellationToken = default);

    Task<ExportReportResult> ExportReportAsync(
        ExportChatReportRequest request,
        CancellationToken cancellationToken = default);
}
