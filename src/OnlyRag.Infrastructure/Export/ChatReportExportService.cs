using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace OnlyRag.Infrastructure.Export;

public sealed class ChatReportExportService : IChatReportExportService
{
    private static readonly Regex UnsafeFileNameChars = new(@"[^A-Za-z0-9._ -]+", RegexOptions.Compiled);
    private readonly AppStoragePaths _storagePaths;
    private readonly PdfExportSettingsStore? _pdfExportSettings;

    public ChatReportExportService(
        AppStoragePaths storagePaths,
        PdfExportSettingsStore? pdfExportSettings = null)
    {
        _storagePaths = storagePaths;
        _pdfExportSettings = pdfExportSettings;
    }

    public Task<ExportPreviewResponse> GeneratePreviewAsync(
        ExportPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string title = string.IsNullOrWhiteSpace(request.Title) ? "RAG_Chat_Report" : request.Title.Trim();

        var chatRequest = new ExportChatReportRequest(
            title,
            request.Format,
            request.Messages,
            request.IncludeCitations,
            request.Notes);

        string htmlPreview = BuildHtmlReport(title, chatRequest);
        int totalCitations = request.Messages.Sum(m => m.Citations?.Count ?? 0);
        int estPages = Math.Max(1, (int)Math.Ceiling(request.Messages.Count * 0.8 + totalCitations * 0.2));
        long estBytes = Encoding.UTF8.GetByteCount(htmlPreview) * 3;

        return Task.FromResult(new ExportPreviewResponse(
            htmlPreview,
            estPages,
            request.Messages.Count,
            totalCitations,
            estBytes,
            request.Theme));
    }

    public Task<ExportReportResult> ExportReportAsync(
        ExportChatReportRequest request,
        CancellationToken cancellationToken = default)
    {
        return ExportChatReportAsync(request, cancellationToken);
    }

    public async Task<ExportReportResult> ExportChatReportAsync(
        ExportChatReportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Messages.Count == 0)
        {
            throw new ArgumentException("Nessun messaggio fornito per l'esportazione del report.");
        }

        Directory.CreateDirectory(_storagePaths.DocumentExportsDirectory);

        string title = string.IsNullOrWhiteSpace(request.Title) ? "RAG_Chat_Report" : request.Title.Trim();
        string sanitizedTitle = SanitizeFileName(title);
        string timestamp = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
        string ext = request.Format == ExportReportFormat.Pdf ? "pdf" : "docx";
        string fileName = $"{sanitizedTitle}_{timestamp}.{ext}";
        string outputPath = Path.Combine(_storagePaths.DocumentExportsDirectory, fileName);

        if (request.Format == ExportReportFormat.Docx)
        {
            WriteDocxReport(outputPath, title, request);
        }
        else
        {
            await WritePdfReportAsync(outputPath, title, request, cancellationToken);
        }

        FileInfo fileInfo = new(outputPath);
        return new ExportReportResult(
            outputPath,
            fileName,
            fileInfo.Exists ? fileInfo.Length : 0,
            request.Format.ToString().ToLowerInvariant());
    }

    private void WriteDocxReport(string outputPath, string title, ExportChatReportRequest request)
    {
        using FileStream stream = new(outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using WordprocessingDocument document = WordprocessingDocument.Create(
            stream,
            DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        MainDocumentPart mainPart = document.AddMainDocumentPart();
        W.Body body = new();
        mainPart.Document = new W.Document(body);

        // Header Title
        body.Append(CreateParagraph(title, "Title"));
        body.Append(CreateParagraph($"Data Esportazione: {DateTimeOffset.Now:dd/MM/yyyy HH:mm}"));
        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            body.Append(CreateParagraph($"Note: {request.Notes}", "Subtitle"));
        }
        body.Append(CreateParagraph(string.Empty));

        // Messages
        foreach (ExportMessageItem msg in request.Messages)
        {
            string roleLabel = msg.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "Utente" : "Assistente OnlyRag";
            body.Append(CreateParagraph($"[{roleLabel}]", "Heading2"));
            body.Append(CreateParagraph(msg.Text, "Normal"));

            if (request.IncludeCitations && msg.Citations != null && msg.Citations.Count > 0)
            {
                body.Append(CreateParagraph("Fonti e Citazioni Utilizzate:", "Heading3"));
                foreach (ExportCitationItem citation in msg.Citations)
                {
                    string pageInfo = citation.PageStart.HasValue ? $" (pag. {citation.PageStart})" : string.Empty;
                    body.Append(CreateParagraph($"• {citation.DocumentName}{pageInfo}: \"{citation.Snippet}\"", "Quote"));
                }
            }

            body.Append(CreateParagraph(string.Empty));
        }

        body.Append(new W.SectionProperties());
        mainPart.Document.Save();
    }

    private async Task WritePdfReportAsync(
        string outputPath,
        string title,
        ExportChatReportRequest request,
        CancellationToken cancellationToken)
    {
        string? customPath = null;
        if (_pdfExportSettings != null)
        {
            PdfExportSettings settings = await _pdfExportSettings.GetAsync(cancellationToken);
            customPath = settings.LibreOfficePath;
        }

        string? executable = PdfExportSettingsStore.ResolveLibreOfficeExecutable(customPath);
        string tempDocxPath = Path.Combine(Path.GetTempPath(), $"onlyrag_chat_{Guid.NewGuid():N}.docx");
        string tempOutputDir = Path.Combine(Path.GetTempPath(), $"onlyrag_chat_out_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempOutputDir);

        try
        {
            WriteDocxReport(tempDocxPath, title, request);

            if (executable != null && File.Exists(executable))
            {
                using var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = executable,
                        Arguments = $"--headless --convert-to pdf --outdir \"{tempOutputDir}\" \"{tempDocxPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync(cancellationToken);

                string generatedPdf = Path.Combine(tempOutputDir, Path.ChangeExtension(Path.GetFileName(tempDocxPath), ".pdf"));
                if (File.Exists(generatedPdf))
                {
                    File.Move(generatedPdf, outputPath, overwrite: true);
                    return;
                }
            }

            // Fallback: Generate styled HTML file directly
            string html = BuildHtmlReport(title, request);
            await File.WriteAllTextAsync(outputPath, html, Encoding.UTF8, cancellationToken);
        }
        finally
        {
            if (File.Exists(tempDocxPath)) { try { File.Delete(tempDocxPath); } catch { } }
            if (Directory.Exists(tempOutputDir)) { try { Directory.Delete(tempOutputDir, true); } catch { } }
        }
    }

    private static string BuildHtmlReport(string title, ExportChatReportRequest request)
    {
        StringBuilder sb = new();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"it\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine($"<title>{WebUtility.HtmlEncode(title)}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: 'Segoe UI', Roboto, sans-serif; margin: 40px; color: #1e293b; background: #fff; line-height: 1.6; }");
        sb.AppendLine("h1 { color: #0f172a; border-bottom: 2px solid #3b82f6; padding-bottom: 8px; }");
        sb.AppendLine(".meta { color: #64748b; font-size: 0.9em; margin-bottom: 24px; }");
        sb.AppendLine(".msg { background: #f8fafc; border-left: 4px solid #3b82f6; padding: 16px; margin-bottom: 20px; border-radius: 4px; }");
        sb.AppendLine(".msg.user { border-left-color: #10b981; background: #f0fdf4; }");
        sb.AppendLine(".msg-role { font-weight: bold; margin-bottom: 8px; color: #0f172a; }");
        sb.AppendLine(".citations { margin-top: 12px; padding: 10px; background: #ffffff; border: 1px solid #e2e8f0; border-radius: 4px; font-size: 0.9em; }");
        sb.AppendLine(".citation-item { margin-bottom: 6px; color: #475569; }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine($"<h1>{WebUtility.HtmlEncode(title)}</h1>");
        sb.AppendLine($"<div class=\"meta\">Data: {DateTimeOffset.Now:dd/MM/yyyy HH:mm}</div>");

        foreach (var msg in request.Messages)
        {
            string isUser = msg.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "user" : "";
            string roleName = msg.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "Utente" : "Assistente OnlyRag";
            sb.AppendLine($"<div class=\"msg {isUser}\">");
            sb.AppendLine($"<div class=\"msg-role\">{WebUtility.HtmlEncode(roleName)}</div>");
            sb.AppendLine($"<div>{WebUtility.HtmlEncode(msg.Text).Replace("\n", "<br>")}</div>");

            if (request.IncludeCitations && msg.Citations != null && msg.Citations.Count > 0)
            {
                sb.AppendLine("<div class=\"citations\"><strong>Fonti e Citazioni:</strong>");
                foreach (var c in msg.Citations)
                {
                    string p = c.PageStart.HasValue ? $" (pag. {c.PageStart})" : "";
                    sb.AppendLine($"<div class=\"citation-item\">• <strong>{WebUtility.HtmlEncode(c.DocumentName)}{p}</strong>: <em>{WebUtility.HtmlEncode(c.Snippet)}</em></div>");
                }
                sb.AppendLine("</div>");
            }

            sb.AppendLine("</div>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static W.Paragraph CreateParagraph(string text, string? styleId = null)
    {
        W.Paragraph paragraph = new(new W.Run(new W.Text(text) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve }));
        if (styleId is not null)
        {
            paragraph.ParagraphProperties = new W.ParagraphProperties(
                new W.ParagraphStyleId { Val = styleId });
        }
        return paragraph;
    }

    private static string SanitizeFileName(string fileName)
    {
        string sanitized = UnsafeFileNameChars.Replace(fileName, "_");
        return string.IsNullOrWhiteSpace(sanitized) ? "Chat_Report" : sanitized;
    }
}
