using System.Text;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Api;

public sealed partial class TranslationExportService
{
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
}
