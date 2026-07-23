using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ingestion;
using OnlyRag.Infrastructure.Storage;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace OnlyRag.Api;

public sealed class TranslationExportService
{
    private static readonly Regex UnsafeSegmentCharacters = new(@"[^A-Za-z0-9._ -]+", RegexOptions.Compiled);
    private readonly InProcessBackendDescriptor descriptor;
    private readonly ITranslationRepository translations;
    private readonly IPdfExportConverter pdfExportConverter;

    public TranslationExportService(
        InProcessBackendDescriptor descriptor,
        ITranslationRepository translations,
        IPdfExportConverter pdfExportConverter)
    {
        this.descriptor = descriptor;
        this.translations = translations;
        this.pdfExportConverter = pdfExportConverter;
    }

    public async Task<TranslationExportResponse?> ExportAsync(
        long translationId,
        TranslationExportRequest request,
        CancellationToken cancellationToken = default)
    {
        StoredTranslation? translation = await translations.GetAsync(translationId, cancellationToken);
        if (translation is null)
        {
            return null;
        }

        string format = NormalizeFormat(request.Format);
        IReadOnlyList<StoredTranslationUnit> units = await translations.ListUnitsAsync(
            translationId,
            cancellationToken);
        ValidateExportCompleteness(translation, units);
        string outputPath = CreateOutputPath(translation, format);

        switch (format)
        {
            case "txt":
                await WriteTextAsync(outputPath, BuildPlainText(translation, units), cancellationToken);
                break;
            case "markdown":
                await WriteTextAsync(outputPath, BuildMarkdown(translation, units), cancellationToken);
                break;
            case "html":
                await WriteTextAsync(outputPath, BuildHtml(translation, units), cancellationToken);
                break;
            case "docx":
                WriteDocx(outputPath, translation, units);
                break;
            case "pdf":
                await WritePdfAsync(outputPath, translation, units, cancellationToken);
                break;
        }

        return new TranslationExportResponse(translationId, format, outputPath, "Completed");
    }

    private static void ValidateExportCompleteness(
        StoredTranslation translation,
        IReadOnlyList<StoredTranslationUnit> units)
    {
        if (!string.Equals(translation.Status, "Completed", StringComparison.Ordinal))
        {
            throw new TranslationExportException(
                "Traduzione incompleta",
                "Completa la traduzione prima di esportarla.");
        }

        if (units.Count == 0 || units.Count != translation.UnitCount)
        {
            throw new TranslationExportException(
                "Traduzione incompleta",
                "Le unita della traduzione non sono complete. Riprova dopo il completamento del job.");
        }

        bool hasIncompleteUnit = units.Any(unit =>
            unit.Status is not ("Completed" or "Corrected")
            || string.IsNullOrWhiteSpace(unit.TranslatedText)
                && string.IsNullOrWhiteSpace(unit.MachineTranslatedText));
        if (hasIncompleteUnit)
        {
            throw new TranslationExportException(
                "Traduzione incompleta",
                "Una o piu unita non hanno testo tradotto. Correggi o rilancia la traduzione prima di esportarla.");
        }
    }

    private string CreateOutputPath(StoredTranslation translation, string format)
    {
        Directory.CreateDirectory(descriptor.StoragePaths.DocumentExportsDirectory);
        string extension = format switch
        {
            "txt" => "txt",
            "markdown" => "md",
            "html" => "html",
            "docx" => "docx",
            "pdf" => "pdf",
            _ => throw new InvalidOperationException("Formato export non supportato.")
        };
        string originalName = SanitizeSegment(Path.GetFileNameWithoutExtension(translation.DocumentName), "documento", 80);
        string targetLanguage = SanitizeSegment(translation.TargetLanguage, "target", 40);
        string timestamp = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
        string baseName = $"{originalName}_{targetLanguage}_{timestamp}";

        for (int index = 0; index < 100; index++)
        {
            string suffix = index == 0 ? string.Empty : $"_{index:D2}";
            string fileName = $"{baseName}{suffix}.{extension}";
            string path = SafeDocumentPath.ResolveWithinRoot(descriptor.StoragePaths.DocumentExportsDirectory, fileName);
            if (!File.Exists(path))
            {
                return path;
            }
        }

        throw new TranslationExportException(
            "Export non creato",
            "Non e stato possibile generare un nome file export libero.");
    }

    private static async Task WriteTextAsync(
        string outputPath,
        string content,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await using StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(content.AsMemory(), cancellationToken);
    }

    private static string BuildPlainText(
        StoredTranslation translation,
        IReadOnlyList<StoredTranslationUnit> units)
    {
        StringBuilder builder = new();
        builder.Append("Documento: ").AppendLine(translation.DocumentName);
        builder.Append("Lingua target: ").AppendLine(translation.TargetLanguage);
        builder.Append("Modello: ").AppendLine(translation.Model);

        int? currentPage = null;
        bool inTable = false;
        foreach (StoredTranslationUnit unit in OrderedUnits(units))
        {
            if (unit.PageNumber != currentPage)
            {
                currentPage = unit.PageNumber;
                inTable = false;
                builder.AppendLine();
                builder.Append("=== ").Append(PageHeading(currentPage)).AppendLine(" ===");
                builder.AppendLine();
            }

            if (unit.UnitKind == "table-cell")
            {
                if (!inTable)
                {
                    builder.AppendLine("Tabella:");
                    inTable = true;
                }

                builder.Append("- ").AppendLine(ExportText(unit));
                continue;
            }

            inTable = false;
            builder.AppendLine(ExportText(unit));
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildHtml(
        StoredTranslation translation,
        IReadOnlyList<StoredTranslationUnit> units)
    {
        StringBuilder builder = new();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"it\">");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\">");
        builder.Append("<title>").Append(Html(translation.DocumentName)).AppendLine("</title>");
        builder.AppendLine("<style>");
        builder.AppendLine("body{font-family:'Segoe UI',Arial,sans-serif;line-height:1.55;margin:32px;color:#111827;background:#fff;}");
        builder.AppendLine("main{max-width:900px;margin:0 auto;}");
        builder.AppendLine("h1{font-size:28px;margin:0 0 8px;} h2{font-size:20px;margin:32px 0 12px;border-bottom:1px solid #d1d5db;padding-bottom:6px;}");
        builder.AppendLine(".meta{color:#4b5563;margin:0 0 24px;} p{margin:0 0 12px;white-space:pre-wrap;} table{width:100%;border-collapse:collapse;margin:8px 0 16px;} td{border:1px solid #d1d5db;padding:8px;vertical-align:top;}");
        builder.AppendLine("@media print{body{margin:18mm;} section.page{break-after:page;} section.page:last-child{break-after:auto;}}");
        builder.AppendLine("</style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("<main>");
        builder.Append("<h1>").Append(Html(translation.DocumentName)).AppendLine("</h1>");
        builder.Append("<p class=\"meta\">Lingua target: ").Append(Html(translation.TargetLanguage))
            .Append(" | Modello: ").Append(Html(translation.Model)).AppendLine("</p>");

        int? currentPage = null;
        bool pageOpen = false;
        List<string> tableCells = [];
        foreach (StoredTranslationUnit unit in OrderedUnits(units))
        {
            if (unit.PageNumber != currentPage)
            {
                AppendPendingHtmlTable(builder, tableCells);
                if (pageOpen)
                {
                    builder.AppendLine("</section>");
                }

                currentPage = unit.PageNumber;
                pageOpen = true;
                builder.Append("<section class=\"page\" data-source-page=\"")
                    .Append(Html(currentPage?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty))
                    .AppendLine("\">");
                builder.Append("<h2>").Append(Html(PageHeading(currentPage))).AppendLine("</h2>");
            }

            if (unit.UnitKind == "table-cell")
            {
                tableCells.Add(ExportText(unit));
                continue;
            }

            AppendPendingHtmlTable(builder, tableCells);
            builder.Append("<p data-unit-index=\"")
                .Append(unit.UnitIndex.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append("\" data-unit-kind=\"")
                .Append(Html(unit.UnitKind))
                .Append("\">")
                .Append(Html(ExportText(unit)))
                .AppendLine("</p>");
        }

        AppendPendingHtmlTable(builder, tableCells);
        if (pageOpen)
        {
            builder.AppendLine("</section>");
        }

        builder.AppendLine("</main>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static string BuildMarkdown(
        StoredTranslation translation,
        IReadOnlyList<StoredTranslationUnit> units)
    {
        StringBuilder builder = new();
        builder.Append("# ").AppendLine(EscapeMarkdownText(translation.DocumentName));
        builder.AppendLine();
        builder.Append("- Lingua target: ").AppendLine(EscapeMarkdownText(translation.TargetLanguage));
        builder.Append("- Modello: ").AppendLine(EscapeMarkdownText(translation.Model));

        int? currentPage = null;
        bool inTable = false;
        foreach (StoredTranslationUnit unit in OrderedUnits(units))
        {
            if (unit.PageNumber != currentPage)
            {
                currentPage = unit.PageNumber;
                inTable = false;
                builder.AppendLine();
                builder.Append("## ").AppendLine(EscapeMarkdownText(PageHeading(currentPage)));
                builder.AppendLine();
            }

            if (unit.UnitKind == "table-cell")
            {
                if (!inTable)
                {
                    builder.AppendLine("| Cella tradotta |");
                    builder.AppendLine("| --- |");
                    inTable = true;
                }

                builder.Append("| ").Append(EscapeMarkdownTableCell(ExportText(unit))).AppendLine(" |");
                continue;
            }

            inTable = false;
            builder.AppendLine(ExportText(unit));
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static void WriteDocx(
        string outputPath,
        StoredTranslation translation,
        IReadOnlyList<StoredTranslationUnit> units)
    {
        using FileStream stream = new(outputPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using WordprocessingDocument document = WordprocessingDocument.Create(
            stream,
            DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        MainDocumentPart mainPart = document.AddMainDocumentPart();
        W.Body body = new();
        mainPart.Document = new W.Document(body);

        body.Append(CreateParagraph(translation.DocumentName, "Title"));
        body.Append(CreateParagraph($"Lingua target: {translation.TargetLanguage}"));
        body.Append(CreateParagraph($"Modello: {translation.Model}"));

        int? currentPage = null;
        List<string> tableCells = [];
        foreach (StoredTranslationUnit unit in OrderedUnits(units))
        {
            if (unit.PageNumber != currentPage)
            {
                AppendPendingTable(body, tableCells);
                bool hasPreviousPage = currentPage is not null;
                currentPage = unit.PageNumber;
                body.Append(CreatePageHeadingParagraph(PageHeading(currentPage), hasPreviousPage));
            }

            if (unit.UnitKind == "table-cell")
            {
                tableCells.Add(ExportText(unit));
                continue;
            }

            AppendPendingTable(body, tableCells);
            body.Append(CreateParagraph(ExportText(unit), UnitStyle(unit)));
        }

        AppendPendingTable(body, tableCells);
        body.Append(new W.SectionProperties());
        mainPart.Document.Save();
    }

    private static void AppendPendingTable(W.Body body, List<string> tableCells)
    {
        if (tableCells.Count == 0)
        {
            return;
        }

        W.Table table = new(
            new W.TableProperties(
                new W.TableBorders(
                    new W.TopBorder { Val = W.BorderValues.Single, Size = 4 },
                    new W.BottomBorder { Val = W.BorderValues.Single, Size = 4 },
                    new W.LeftBorder { Val = W.BorderValues.Single, Size = 4 },
                    new W.RightBorder { Val = W.BorderValues.Single, Size = 4 },
                    new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 4 },
                    new W.InsideVerticalBorder { Val = W.BorderValues.Single, Size = 4 })));

        foreach (string cell in tableCells)
        {
            table.Append(new W.TableRow(
                new W.TableCell(
                    CreateParagraph(cell, "Normal"),
                    new W.TableCellProperties(new W.TableCellWidth { Type = W.TableWidthUnitValues.Auto }))));
        }

        body.Append(table);
        tableCells.Clear();
    }

    private static void AppendPendingHtmlTable(StringBuilder builder, List<string> tableCells)
    {
        if (tableCells.Count == 0)
        {
            return;
        }

        builder.AppendLine("<table>");
        foreach (string cell in tableCells)
        {
            builder.Append("<tr><td data-unit-kind=\"table-cell\">").Append(Html(cell)).AppendLine("</td></tr>");
        }

        builder.AppendLine("</table>");
        tableCells.Clear();
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

    private static W.Paragraph CreatePageHeadingParagraph(string text, bool pageBreakBefore)
    {
        W.Paragraph paragraph = CreateParagraph(text, "Heading1");
        paragraph.ParagraphProperties ??= new W.ParagraphProperties();
        if (pageBreakBefore)
        {
            paragraph.ParagraphProperties.PrependChild(new W.PageBreakBefore());
        }

        return paragraph;
    }

    private static string UnitStyle(StoredTranslationUnit unit)
    {
        return unit.UnitKind switch
        {
            "textbox" => "Quote",
            _ => "Normal"
        };
    }

    private static IEnumerable<StoredTranslationUnit> OrderedUnits(IReadOnlyList<StoredTranslationUnit> units)
    {
        return units.OrderBy(unit => unit.UnitIndex);
    }

    private static string ExportText(StoredTranslationUnit unit)
    {
        return FirstNonBlank(unit.TranslatedText, unit.MachineTranslatedText, unit.SourceText);
    }

    private static string FirstNonBlank(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string PageHeading(int? pageNumber)
    {
        return pageNumber.HasValue ? $"Pagina {pageNumber.Value}" : "Sezione senza pagina";
    }

    private static string EscapeMarkdownText(string text)
    {
        return text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);
    }

    private static string EscapeMarkdownTableCell(string text)
    {
        return text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r\n", "<br>", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal)
            .Replace("\r", "<br>", StringComparison.Ordinal);
    }

    private static string NormalizeFormat(string format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            throw new TranslationExportException("Formato export non valido", "Specifica un formato export.");
        }

        return format.Trim().ToLowerInvariant() switch
        {
            "txt" => "txt",
            "md" or "markdown" => "markdown",
            "htm" or "html" => "html",
            "docx" => "docx",
            "pdf" => "pdf",
            _ => throw new TranslationExportException(
                "Formato export non supportato",
                "Formati supportati: txt, markdown, html, docx, pdf.")
        };
    }

    private async Task WritePdfAsync(
        string outputPath,
        StoredTranslation translation,
        IReadOnlyList<StoredTranslationUnit> units,
        CancellationToken cancellationToken)
    {
        string tempDir = Path.Combine(descriptor.StoragePaths.TempDirectory, "translation-export", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        PdfExportConversionResult? conversion = null;
        try
        {
            string tempDocx = Path.Combine(tempDir, "export.docx");
            WriteDocx(tempDocx, translation, units);

            conversion = await pdfExportConverter.ConvertToPdfAsync(
                new PdfExportConversionRequest(
                    translation.Id,
                    tempDocx,
                    "export.docx",
                    "docx"),
                cancellationToken);

            File.Move(conversion.PdfPath, outputPath, overwrite: false);
        }
        catch (PdfExportConversionUnavailableException ex)
        {
            string detail = UserFacingErrorText.FromExternalDetail(
                ex.Message,
                "LibreOffice non disponibile.");
            throw new TranslationExportException(
                "LibreOffice non disponibile",
                $"L'export PDF richiede LibreOffice. Configura il percorso LibreOffice nelle impostazioni. Dettaglio: {detail}");
        }
        catch (PdfExportConversionException ex)
        {
            string detail = UserFacingErrorText.FromExternalDetail(
                ex.Message,
                "Dettagli tecnici disponibili nei log locali.");
            throw new TranslationExportException(
                "Conversione PDF fallita",
                $"LibreOffice non ha completato la conversione. Dettaglio: {detail}");
        }
        finally
        {
            if (conversion is not null)
            {
                TryDeleteDirectory(conversion.TemporaryDirectory);
            }

            TryDeleteDirectory(tempDir);
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup; export has already failed or completed.
        }
    }

    private static string SanitizeSegment(string? value, string fallback, int maxLength)
    {
        string normalized = string.IsNullOrWhiteSpace(value)
            ? fallback
            : UnsafeSegmentCharacters.Replace(value.Trim(), "_");
        normalized = string.Join(
            "_",
            normalized.Split([' ', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        normalized = normalized.Trim('.', ' ');
        if (normalized.Length == 0)
        {
            normalized = fallback;
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength].Trim('.', ' ');
    }

    private static string Html(string text)
    {
        return WebUtility.HtmlEncode(text);
    }
}

public sealed class TranslationExportException : Exception
{
    public TranslationExportException(string title, string message)
        : base(message)
    {
        Title = title;
    }

    public string Title { get; }
}
