using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Api;

public sealed partial class TranslationExportService
{
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
}
