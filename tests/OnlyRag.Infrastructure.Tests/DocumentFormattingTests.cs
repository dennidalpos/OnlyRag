using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;
using OnlyRag.Infrastructure.Ingestion;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public sealed class DocumentFormattingTests
{
    [Fact]
    public void OfficeOpenXmlTextExtractor_DocxTable_FormatsAsMarkdownTable()
    {
        string path = Path.GetTempFileName() + ".docx";
        try
        {
            using (WordprocessingDocument doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
            {
                MainDocumentPart mainPart = doc.AddMainDocumentPart();
                mainPart.Document = new W.Document(
                    new W.Body(
                        new W.Table(
                            new W.TableRow(
                                new W.TableCell(new W.Paragraph(new W.Run(new W.Text("Intestazione 1")))),
                                new W.TableCell(new W.Paragraph(new W.Run(new W.Text("Intestazione 2"))))
                            ),
                            new W.TableRow(
                                new W.TableCell(new W.Paragraph(new W.Run(new W.Text("Valore 1")))),
                                new W.TableCell(new W.Paragraph(new W.Run(new W.Text("Valore 2"))))
                            )
                        )
                    )
                );
            }

            OfficeOpenXmlTextExtractor extractor = new();
            IReadOnlyList<OfficeOpenXmlTextUnit> units = extractor.Extract(path, ".docx");

            Assert.NotEmpty(units);
            string text = units[0].Text;
            Assert.Contains("| Intestazione 1 | Intestazione 2 |", text);
            Assert.Contains("| --- | --- |", text);
            Assert.Contains("| Valore 1 | Valore 2 |", text);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void OfficeOpenXmlTextExtractor_XlsxSheet_FormatsAsMarkdownTable()
    {
        string path = Path.GetTempFileName() + ".xlsx";
        try
        {
            using (SpreadsheetDocument doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook))
            {
                WorkbookPart workbookPart = doc.AddWorkbookPart();
                workbookPart.Workbook = new S.Workbook();
                WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                worksheetPart.Worksheet = new S.Worksheet(
                    new S.SheetData(
                        new S.Row(
                            new S.Cell { DataType = S.CellValues.InlineString, InlineString = new S.InlineString(new S.Text("Col A")) },
                            new S.Cell { DataType = S.CellValues.InlineString, InlineString = new S.InlineString(new S.Text("Col B")) }
                        ),
                        new S.Row(
                            new S.Cell { DataType = S.CellValues.InlineString, InlineString = new S.InlineString(new S.Text("100")) },
                            new S.Cell { DataType = S.CellValues.InlineString, InlineString = new S.InlineString(new S.Text("200")) }
                        )
                    )
                );

                S.Sheets sheets = doc.WorkbookPart.Workbook.AppendChild(new S.Sheets());
                sheets.Append(new S.Sheet
                {
                    Id = doc.WorkbookPart.GetIdOfPart(worksheetPart),
                    SheetId = 1,
                    Name = "Dati"
                });
            }

            OfficeOpenXmlTextExtractor extractor = new();
            IReadOnlyList<OfficeOpenXmlTextUnit> units = extractor.Extract(path, ".xlsx");

            Assert.NotEmpty(units);
            string text = units[0].Text;
            Assert.Contains("## Foglio: Dati", text);
            Assert.Contains("| Col A | Col B |", text);
            Assert.Contains("| --- | --- |", text);
            Assert.Contains("| 100 | 200 |", text);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
