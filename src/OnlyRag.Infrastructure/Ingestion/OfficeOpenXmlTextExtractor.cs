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
                    current = new(paragraphText);
                    current.AppendHeading(paragraphText);
                    continue;
                }

                if (IsHeadingParagraph(paragraph))
                {
                    current.Title = paragraphText;
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
            StringBuilder contentBuilder = new();

            S.Worksheet worksheetDocument = worksheet.Worksheet
                ?? throw new InvalidOperationException($"Il foglio '{sheetName}' non ha contenuto leggibile.");
            foreach (S.Row row in worksheetDocument.Descendants<S.Row>())
            {
                List<string> cells = [];
                foreach (S.Cell cell in row.Elements<S.Cell>())
                {
                    string value = ExtractSpreadsheetCellValue(cell, sharedStrings).Trim();
                    if (value.Length == 0)
                    {
                        continue;
                    }

                    string coordinate = cell.CellReference?.Value ?? $"R{row.RowIndex?.Value ?? 0}";
                    cells.Add($"[{coordinate}] {value}");
                }

                if (cells.Count > 0)
                {
                    contentBuilder.Append("Riga ").Append(row.RowIndex?.Value ?? 0).Append(": ")
                        .AppendLine(string.Join(" | ", cells));
                }
            }

            string text = contentBuilder.Length == 0
                ? string.Empty
                : $"Foglio: {sheetName}{Environment.NewLine}{contentBuilder}".Trim();
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
            AppendDrawingParagraphs(contentBuilder, "Textbox", slide.Descendants<A.Paragraph>());

            NotesSlidePart? notesPart = slidePart.NotesSlidePart;
            if (notesPart?.NotesSlide is not null)
            {
                AppendDrawingParagraphs(contentBuilder, "Note", notesPart.NotesSlide.Descendants<A.Paragraph>());
            }

            string text = contentBuilder.Length == 0
                ? string.Empty
                : $"Slide {slideNumber}{Environment.NewLine}{contentBuilder}".Trim();
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
        StringBuilder builder = new();
        int rowNumber = 0;

        foreach (W.TableRow row in table.Elements<W.TableRow>())
        {
            rowNumber++;
            List<string> cells = [];
            int cellNumber = 0;
            foreach (W.TableCell cell in row.Elements<W.TableCell>())
            {
                cellNumber++;
                string cellText = string.Join(
                    " ",
                    cell.Elements<W.Paragraph>()
                        .Select(ExtractWordParagraphText)
                        .Where(text => !string.IsNullOrWhiteSpace(text)))
                    .Trim();
                cells.Add($"Cella {cellNumber}: {cellText}");
            }

            builder.Append("Riga ").Append(rowNumber).Append(": ")
                .AppendLine(string.Join(" | ", cells));
        }

        return builder.ToString().Trim();
    }

    private static string ExtractWordParagraphText(W.Paragraph paragraph)
    {
        StringBuilder builder = new();
        foreach (OpenXmlElement element in paragraph.Descendants())
        {
            switch (element)
            {
                case W.Text text:
                    builder.Append(text.Text);
                    break;
                case W.TabChar:
                    builder.Append('\t');
                    break;
                case W.Break:
                    builder.AppendLine();
                    break;
            }
        }

        return builder.ToString();
    }

    private static bool IsHeadingParagraph(W.Paragraph paragraph)
    {
        string? styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        return styleId is not null
            && (styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)
                || styleId.Contains("Title", StringComparison.OrdinalIgnoreCase)
                || styleId.Contains("Titolo", StringComparison.OrdinalIgnoreCase));
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
        string label,
        IEnumerable<A.Paragraph> paragraphs)
    {
        int index = 0;
        foreach (A.Paragraph paragraph in paragraphs)
        {
            string text = string.Concat(paragraph.Descendants<A.Text>().Select(item => item.Text)).Trim();
            if (text.Length == 0)
            {
                continue;
            }

            index++;
            builder.Append(label).Append(' ').Append(index).Append(": ").AppendLine(text);
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
            AppendParagraph($"Titolo: {text}");
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

            AppendParagraph($"Tabella:\n{text}");
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
