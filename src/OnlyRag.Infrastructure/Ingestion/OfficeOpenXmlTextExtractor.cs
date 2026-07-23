using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace OnlyRag.Infrastructure.Ingestion;

public sealed class OfficeOpenXmlTextExtractor
{
    public IReadOnlyList<OfficeOpenXmlTextUnit> Extract(string path, string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);

        return extension.ToLowerInvariant() switch
        {
            ".docx" => ExtractDocx(path),
            ".xlsx" => ExtractXlsx(path),
            ".pptx" => ExtractPptx(path),
            _ => throw new NotSupportedException($"Formato Office Open XML non supportato: {extension}.")
        };
    }

    private static IReadOnlyList<OfficeOpenXmlTextUnit> ExtractDocx(string path)
    {
        using WordprocessingDocument document = WordprocessingDocument.Open(path, isEditable: false);
        W.Document? wordDocument = document.MainDocumentPart?.Document;
        W.Body? body = wordDocument?.Body;
        if (body is null)
        {
            throw new InvalidOperationException("Il DOCX non contiene un corpo documento leggibile.");
        }

        List<DocxSectionBuilder> sections = [];
        DocxSectionBuilder current = new("Sezione logica 1");

        foreach (OpenXmlElement element in body.Elements())
        {
            if (element is W.Paragraph paragraph)
            {
                string paragraphText = ExtractWordParagraphText(paragraph).Trim();
                if (paragraphText.Length == 0)
                {
                    continue;
                }

                if (IsHeadingParagraph(paragraph) && current.HasContent)
                {
                    sections.Add(current);
                    string headingTitle = paragraphText.TrimStart('#', ' ').Trim();
                    current = new(headingTitle);
                    current.AppendHeading(paragraphText);
                    continue;
                }

                if (IsHeadingParagraph(paragraph))
                {
                    current.Title = paragraphText.TrimStart('#', ' ').Trim();
                    current.AppendHeading(paragraphText);
                }
                else
                {
                    current.AppendParagraph(paragraphText);
                }

                continue;
            }

            if (element is W.Table table)
            {
                current.AppendTable(ExtractWordTableText(table));
            }
        }

        if (current.HasContent || sections.Count == 0)
        {
            sections.Add(current);
        }

        return sections
            .Select((section, index) => new OfficeOpenXmlTextUnit(
                index + 1,
                "section",
                section.Title,
                section.BuildText()))
            .ToList();
    }

    private static IReadOnlyList<OfficeOpenXmlTextUnit> ExtractXlsx(string path)
    {
        using SpreadsheetDocument document = SpreadsheetDocument.Open(path, isEditable: false);
        WorkbookPart workbook = document.WorkbookPart
            ?? throw new InvalidOperationException("L'XLSX non contiene una cartella di lavoro leggibile.");
        S.SharedStringTable? sharedStrings = workbook.SharedStringTablePart?.SharedStringTable;
        S.Workbook workbookDocument = workbook.Workbook
            ?? throw new InvalidOperationException("L'XLSX non contiene una cartella di lavoro leggibile.");
        S.Sheets sheets = workbookDocument.Sheets
            ?? throw new InvalidOperationException("L'XLSX non contiene fogli leggibili.");

        List<OfficeOpenXmlTextUnit> units = [];
        int sheetIndex = 0;
        foreach (S.Sheet sheet in sheets.Elements<S.Sheet>())
        {
            sheetIndex++;
            string sheetName = sheet.Name?.Value ?? $"Foglio {sheetIndex}";
            string relationshipId = sheet.Id?.Value
                ?? throw new InvalidOperationException($"Il foglio '{sheetName}' non ha un riferimento valido.");
            WorksheetPart worksheet = (WorksheetPart)workbook.GetPartById(relationshipId);

            S.Worksheet worksheetDocument = worksheet.Worksheet
                ?? throw new InvalidOperationException($"Il foglio '{sheetName}' non ha contenuto leggibile.");

            List<List<string>> rows = [];
            int maxCols = 0;

            foreach (S.Row row in worksheetDocument.Descendants<S.Row>())
            {
                List<string> rowCells = [];
                foreach (S.Cell cell in row.Elements<S.Cell>())
                {
                    string value = ExtractSpreadsheetCellValue(cell, sharedStrings).Trim().Replace("\r", " ").Replace("\n", " ");
                    rowCells.Add(value);
                }

                if (rowCells.Any(c => !string.IsNullOrWhiteSpace(c)))
                {
                    maxCols = Math.Max(maxCols, rowCells.Count);
                    rows.Add(rowCells);
                }
            }

            StringBuilder contentBuilder = new();
            if (rows.Count > 0 && maxCols > 0)
            {
                contentBuilder.AppendLine($"## Foglio: {sheetName}");
                contentBuilder.AppendLine();

                // Format as Markdown Table
                for (int r = 0; r < rows.Count; r++)
                {
                    List<string> row = rows[r];
                    while (row.Count < maxCols)
                    {
                        row.Add(string.Empty);
                    }

                    string rowStr = "| " + string.Join(" | ", row.Select(c => string.IsNullOrWhiteSpace(c) ? "-" : c)) + " |";
                    contentBuilder.AppendLine(rowStr);

                    if (r == 0)
                    {
                        // Add separator header row
                        string separator = "| " + string.Join(" | ", Enumerable.Repeat("---", maxCols)) + " |";
                        contentBuilder.AppendLine(separator);
                    }
                }
            }

            string text = contentBuilder.ToString().Trim();
            units.Add(new OfficeOpenXmlTextUnit(sheetIndex, "sheet", sheetName, text));
        }

        if (units.Count == 0)
        {
            throw new InvalidOperationException("L'XLSX non contiene fogli leggibili.");
        }

        return units;
    }

    private static IReadOnlyList<OfficeOpenXmlTextUnit> ExtractPptx(string path)
    {
        using PresentationDocument document = PresentationDocument.Open(path, isEditable: false);
        PresentationPart presentation = document.PresentationPart
            ?? throw new InvalidOperationException("Il PPTX non contiene una presentazione leggibile.");
        P.Presentation presentationDocument = presentation.Presentation
            ?? throw new InvalidOperationException("Il PPTX non contiene una presentazione leggibile.");
        P.SlideIdList slideIds = presentationDocument.SlideIdList
            ?? throw new InvalidOperationException("Il PPTX non contiene slide leggibili.");

        List<OfficeOpenXmlTextUnit> units = [];
        int slideNumber = 0;
        foreach (P.SlideId slideId in slideIds.Elements<P.SlideId>())
        {
            slideNumber++;
            string relationshipId = slideId.RelationshipId?.Value
                ?? throw new InvalidOperationException($"La slide {slideNumber} non ha un riferimento valido.");
            SlidePart slidePart = (SlidePart)presentation.GetPartById(relationshipId);
            StringBuilder contentBuilder = new();

            P.Slide slide = slidePart.Slide
                ?? throw new InvalidOperationException($"La slide {slideNumber} non ha contenuto leggibile.");

            contentBuilder.AppendLine($"# Slide {slideNumber}");
            contentBuilder.AppendLine();

            AppendDrawingParagraphs(contentBuilder, slide.Descendants<A.Paragraph>());

            NotesSlidePart? notesPart = slidePart.NotesSlidePart;
            if (notesPart?.NotesSlide is not null)
            {
                contentBuilder.AppendLine().AppendLine("### Note slide:");
                AppendDrawingParagraphs(contentBuilder, notesPart.NotesSlide.Descendants<A.Paragraph>());
            }

            string text = contentBuilder.ToString().Trim();
            units.Add(new OfficeOpenXmlTextUnit(
                slideNumber,
                "slide",
                $"Slide {slideNumber}",
                text));
        }

        if (units.Count == 0)
        {
            throw new InvalidOperationException("Il PPTX non contiene slide leggibili.");
        }

        return units;
    }

    private static string ExtractWordTableText(W.Table table)
    {
        List<List<string>> grid = [];
        int maxCols = 0;

        foreach (W.TableRow row in table.Elements<W.TableRow>())
        {
            List<string> rowCells = [];
            foreach (W.TableCell cell in row.Elements<W.TableCell>())
            {
                string cellText = string.Join(
                    " ",
                    cell.Elements<W.Paragraph>()
                        .Select(ExtractWordParagraphText)
                        .Where(text => !string.IsNullOrWhiteSpace(text)))
                    .Trim()
                    .Replace("\r", " ")
                    .Replace("\n", " ");

                rowCells.Add(cellText);
            }

            if (rowCells.Count > 0)
            {
                maxCols = Math.Max(maxCols, rowCells.Count);
                grid.Add(rowCells);
            }
        }

        if (grid.Count == 0 || maxCols == 0)
        {
            return string.Empty;
        }

        StringBuilder tableBuilder = new();
        for (int r = 0; r < grid.Count; r++)
        {
            List<string> row = grid[r];
            while (row.Count < maxCols)
            {
                row.Add(string.Empty);
            }

            string rowLine = "| " + string.Join(" | ", row.Select(c => string.IsNullOrWhiteSpace(c) ? "-" : c)) + " |";
            tableBuilder.AppendLine(rowLine);

            if (r == 0)
            {
                string separator = "| " + string.Join(" | ", Enumerable.Repeat("---", maxCols)) + " |";
                tableBuilder.AppendLine(separator);
            }
        }

        return tableBuilder.ToString().Trim();
    }

    private static string ExtractWordParagraphText(W.Paragraph paragraph)
    {
        StringBuilder builder = new();
        bool isListItem = paragraph.ParagraphProperties?.NumberingProperties is not null;
        string headingPrefix = GetHeadingPrefix(paragraph);

        if (!string.IsNullOrEmpty(headingPrefix))
        {
            builder.Append(headingPrefix).Append(' ');
        }
        else if (isListItem)
        {
            builder.Append("- ");
        }

        foreach (OpenXmlElement element in paragraph.Elements())
        {
            if (element is W.Run run)
            {
                string runText = ExtractRunText(run);
                if (string.IsNullOrEmpty(runText))
                {
                    continue;
                }

                bool isBold = run.RunProperties?.Bold?.Val?.Value ?? (run.RunProperties?.Bold != null);
                bool isItalic = run.RunProperties?.Italic?.Val?.Value ?? (run.RunProperties?.Italic != null);

                if (isBold && isItalic)
                {
                    builder.Append("***").Append(runText.Trim()).Append("*** ");
                }
                else if (isBold)
                {
                    builder.Append("**").Append(runText.Trim()).Append("** ");
                }
                else if (isItalic)
                {
                    builder.Append('*').Append(runText.Trim()).Append("* ");
                }
                else
                {
                    builder.Append(runText);
                }
            }
            else if (element is W.Text textElement)
            {
                builder.Append(textElement.Text);
            }
        }

        string result = builder.ToString().Trim();
        if (isListItem && !result.StartsWith("- ") && !result.StartsWith('#'))
        {
            result = "- " + result;
        }

        return result;
    }

    private static string ExtractRunText(W.Run run)
    {
        StringBuilder runBuilder = new();
        foreach (OpenXmlElement child in run.Elements())
        {
            switch (child)
            {
                case W.Text text:
                    runBuilder.Append(text.Text);
                    break;
                case W.TabChar:
                    runBuilder.Append('\t');
                    break;
                case W.Break:
                    runBuilder.AppendLine();
                    break;
            }
        }
        return runBuilder.ToString();
    }

    private static bool IsHeadingParagraph(W.Paragraph paragraph)
    {
        string? styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        return styleId is not null
            && (styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)
                || styleId.Contains("Title", StringComparison.OrdinalIgnoreCase)
                || styleId.Contains("Titolo", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetHeadingPrefix(W.Paragraph paragraph)
    {
        string? styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (string.IsNullOrWhiteSpace(styleId)) return string.Empty;

        if (styleId.Equals("Heading1", StringComparison.OrdinalIgnoreCase) || styleId.Equals("Heading 1", StringComparison.OrdinalIgnoreCase) || styleId.Equals("Title", StringComparison.OrdinalIgnoreCase) || styleId.Equals("Titolo", StringComparison.OrdinalIgnoreCase))
        {
            return "#";
        }
        if (styleId.Equals("Heading2", StringComparison.OrdinalIgnoreCase) || styleId.Equals("Heading 2", StringComparison.OrdinalIgnoreCase) || styleId.Equals("Subtitle", StringComparison.OrdinalIgnoreCase))
        {
            return "##";
        }
        if (styleId.Equals("Heading3", StringComparison.OrdinalIgnoreCase) || styleId.Equals("Heading 3", StringComparison.OrdinalIgnoreCase))
        {
            return "###";
        }
        if (styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
        {
            return "##";
        }

        return string.Empty;
    }

    private static string ExtractSpreadsheetCellValue(S.Cell cell, S.SharedStringTable? sharedStrings)
    {
        string rawValue = cell.CellValue?.InnerText ?? cell.InnerText;
        if (cell.DataType is null)
        {
            return rawValue;
        }

        S.CellValues dataType = cell.DataType.Value;
        if (dataType == S.CellValues.SharedString)
        {
            return int.TryParse(rawValue, out int index) && sharedStrings is not null
                ? sharedStrings.ElementAt(index).InnerText
                : rawValue;
        }

        if (dataType == S.CellValues.Boolean)
        {
            return rawValue == "1" ? "TRUE" : "FALSE";
        }

        if (dataType == S.CellValues.InlineString)
        {
            return cell.InlineString?.InnerText ?? rawValue;
        }

        return rawValue;
    }

    private static void AppendDrawingParagraphs(
        StringBuilder builder,
        IEnumerable<A.Paragraph> paragraphs)
    {
        foreach (A.Paragraph paragraph in paragraphs)
        {
            string text = string.Concat(paragraph.Descendants<A.Text>().Select(item => item.Text)).Trim();
            if (text.Length == 0)
            {
                continue;
            }

            builder.AppendLine($"- {text}");
        }
    }

    private sealed class DocxSectionBuilder
    {
        private readonly StringBuilder builder = new();

        public DocxSectionBuilder(string title)
        {
            Title = title;
        }

        public string Title { get; set; }

        public bool HasContent => builder.Length > 0;

        public void AppendHeading(string text)
        {
            string headerLine = text.StartsWith('#') ? text : $"# {text}";
            AppendParagraph(headerLine);
        }

        public void AppendParagraph(string text)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine().AppendLine();
            }

            builder.Append(text);
        }

        public void AppendTable(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            AppendParagraph(text);
        }

        public string BuildText()
        {
            return builder.ToString().Trim();
        }
    }
}

public sealed record OfficeOpenXmlTextUnit(
    int UnitNumber,
    string UnitKind,
    string Name,
    string Text);
