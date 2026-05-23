using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ingestion;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Api;

public sealed partial class TranslationExportService
{
    private static readonly Regex UnsafeSegmentCharacters = new(@"[^A-Za-z0-9._ -]+", RegexOptions.Compiled);
    private readonly InProcessBackendDescriptor descriptor;
    private readonly ITranslationRepository translations;
    private readonly IOfficeConversionService officeConverter;

    public TranslationExportService(
        InProcessBackendDescriptor descriptor,
        ITranslationRepository translations,
        IOfficeConversionService officeConverter)
    {
        this.descriptor = descriptor;
        this.translations = translations;
        this.officeConverter = officeConverter;
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
        OfficeConversionResult? conversion = null;
        try
        {
            string tempDocx = Path.Combine(tempDir, "export.docx");
            WriteDocx(tempDocx, translation, units);

            conversion = await officeConverter.ConvertToPdfAsync(
                new OfficeConversionRequest(
                    translation.Id,
                    tempDocx,
                    "export.docx",
                    "docx"),
                cancellationToken);

            File.Move(conversion.PdfPath, outputPath, overwrite: false);
        }
        catch (OfficeConversionUnavailableException ex)
        {
            string detail = UserFacingErrorText.FromExternalDetail(
                ex.Message,
                "LibreOffice non disponibile.");
            throw new TranslationExportException(
                "LibreOffice non disponibile",
                $"L'export PDF richiede LibreOffice. Configura il percorso LibreOffice nelle impostazioni. Dettaglio: {detail}");
        }
        catch (OfficeConversionException ex)
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
