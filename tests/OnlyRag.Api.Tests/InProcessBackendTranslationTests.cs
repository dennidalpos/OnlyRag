using System.Net;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;
using OnlyRag.Api;
using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Ingestion;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Api.Tests;

public sealed partial class InProcessBackendTests
{
    [Fact]
    public async Task TranslationCompare_ReturnsPageUnitsAndSavesManualCorrection()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("disabled-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        LocalSqliteConnectionFactory connectionFactory = new(tempDescriptor.Descriptor.Store);
        SqliteDocumentRepository documentRepository = new(connectionFactory);
        SqliteTranslationRepository translationRepository = new(connectionFactory);
        ImportedDocument document = await CreateIndexedDocumentAsync(documentRepository, tempDescriptor.Root);
        IReadOnlyList<TranslationSourceUnit> units = await translationRepository.BuildSourceUnitsAsync(document.Id);
        StoredTranslation translation = await translationRepository.CreateAsync(
            document.Id,
            "English",
            "llama-test",
            jobId: null,
            units,
            CancellationToken.None);
        StoredTranslationUnit firstUnit = (await translationRepository.ListUnitsAsync(translation.Id))[0];
        await translationRepository.SaveUnitSuccessAsync(firstUnit.Id, "Machine page one", null);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        TranslationCompareResponse? compare = await httpClient.GetFromJsonAsync<TranslationCompareResponse>(
            $"/api/translations/{translation.Id}/compare?page=1",
            JsonOptions);
        using HttpResponseMessage putResponse = await httpClient.PutAsJsonAsync(
            $"/api/translations/{translation.Id}/units/{firstUnit.Id}",
            new UpdateTranslationUnitRequest("Manual page one"),
            JsonOptions);
        TranslationUnitResponse? corrected = await putResponse.Content.ReadFromJsonAsync<TranslationUnitResponse>(JsonOptions);

        Assert.NotNull(compare);
        Assert.Equal(1, compare.CurrentPage);
        Assert.Equal(2, compare.PageCount);
        Assert.Single(compare.Units);
        Assert.Equal("Sezione 1 - Paragrafo 1", compare.Units[0].DisplayLabel);
        Assert.Equal("Machine page one", compare.Units[0].TranslatedText);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        Assert.NotNull(corrected);
        Assert.Equal("Manual page one", corrected.TranslatedText);
        Assert.Equal("Machine page one", corrected.MachineTranslatedText);
        Assert.True(corrected.ManuallyEdited);
    }

    [Fact]
    public async Task TranslationExport_PostTxt_WritesSafeFileInExportsAndPreservesUnitOrder()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("disabled-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        SeededTranslation seeded = await CreateCompletedTranslationAsync(tempDescriptor, "report:2026?.txt");
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            $"/api/translations/{seeded.Translation.Id}/export",
            new TranslationExportRequest("txt"),
            JsonOptions);
        TranslationExportResponse? exported = await response.Content.ReadFromJsonAsync<TranslationExportResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(exported);
        Assert.Equal("Completed", exported.Status);
        Assert.Equal("txt", exported.Format);
        Assert.StartsWith(
            Path.GetFullPath(tempDescriptor.Descriptor.StoragePaths.DocumentExportsDirectory),
            Path.GetFullPath(exported.OutputPath),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(':', Path.GetFileName(exported.OutputPath));
        Assert.DoesNotContain('?', Path.GetFileName(exported.OutputPath));
        Assert.True(File.Exists(exported.OutputPath));

        string content = await File.ReadAllTextAsync(exported.OutputPath, Encoding.UTF8);
        Assert.Contains("=== Pagina 1 ===", content, StringComparison.Ordinal);
        Assert.Contains("=== Pagina 2 ===", content, StringComparison.Ordinal);
        Assert.True(content.IndexOf("Prima traduzione", StringComparison.Ordinal) < content.IndexOf("Seconda traduzione", StringComparison.Ordinal));
        Assert.True(content.IndexOf("Seconda traduzione", StringComparison.Ordinal) < content.IndexOf("Nome tradotto", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TranslationExport_PostMarkdown_WritesPageSeparatorsAndReadableTable()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("disabled-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        SeededTranslation seeded = await CreateCompletedTranslationAsync(tempDescriptor, "table-source.txt");
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            $"/api/translations/{seeded.Translation.Id}/export",
            new TranslationExportRequest("markdown"),
            JsonOptions);
        TranslationExportResponse? exported = await response.Content.ReadFromJsonAsync<TranslationExportResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(exported);
        Assert.EndsWith(".md", exported.OutputPath, StringComparison.OrdinalIgnoreCase);
        string content = await File.ReadAllTextAsync(exported.OutputPath, Encoding.UTF8);
        Assert.Contains("## Pagina 1", content, StringComparison.Ordinal);
        Assert.Contains("## Pagina 2", content, StringComparison.Ordinal);
        Assert.Contains("| Cella tradotta |", content, StringComparison.Ordinal);
        Assert.Contains("| Nome tradotto |", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranslationExport_PostHtml_WritesSelfContainedPageAndTableMarkup()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("disabled-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        SeededTranslation seeded = await CreateCompletedTranslationAsync(tempDescriptor, "html-source<2026>.txt");
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            $"/api/translations/{seeded.Translation.Id}/export",
            new TranslationExportRequest("html"),
            JsonOptions);
        TranslationExportResponse? exported = await response.Content.ReadFromJsonAsync<TranslationExportResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(exported);
        Assert.Equal("html", exported.Format);
        Assert.EndsWith(".html", exported.OutputPath, StringComparison.OrdinalIgnoreCase);
        string content = await File.ReadAllTextAsync(exported.OutputPath, Encoding.UTF8);
        Assert.Contains("<!doctype html>", content, StringComparison.Ordinal);
        Assert.Contains("html-source&lt;2026&gt;.txt", content, StringComparison.Ordinal);
        Assert.Contains("<section class=\"page\" data-source-page=\"1\">", content, StringComparison.Ordinal);
        Assert.Contains("data-unit-kind=\"paragraph\"", content, StringComparison.Ordinal);
        Assert.Contains("<table>", content, StringComparison.Ordinal);
        Assert.Contains("<td data-unit-kind=\"table-cell\">Nome tradotto</td>", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranslationExport_PostDocx_WritesBasicDocx()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("disabled-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        SeededTranslation seeded = await CreateCompletedTranslationAsync(tempDescriptor, "doc-source.docx");
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            $"/api/translations/{seeded.Translation.Id}/export",
            new TranslationExportRequest("docx"),
            JsonOptions);
        TranslationExportResponse? exported = await response.Content.ReadFromJsonAsync<TranslationExportResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(exported);
        Assert.EndsWith(".docx", exported.OutputPath, StringComparison.OrdinalIgnoreCase);
        using WordprocessingDocument document = WordprocessingDocument.Open(exported.OutputPath, isEditable: false);
        W.Body? body = document.MainDocumentPart?.Document?.Body;

        Assert.NotNull(body);
        Assert.Contains("Pagina 1", body.InnerText, StringComparison.Ordinal);
        Assert.Contains("Prima traduzione", body.InnerText, StringComparison.Ordinal);
        Assert.Contains("Nome tradotto", body.InnerText, StringComparison.Ordinal);
        Assert.NotEmpty(body.Descendants<W.Table>());
        Assert.NotEmpty(body.Descendants<W.PageBreakBefore>());
    }

    [Fact]
    public async Task TranslationExport_PostUnsupportedFormat_ReturnsSupportedFormatsIncludingPdf()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("disabled-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        SeededTranslation seeded = await CreateCompletedTranslationAsync(tempDescriptor, "source.txt");
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            $"/api/translations/{seeded.Translation.Id}/export",
            new TranslationExportRequest("rtf"),
            JsonOptions);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("txt, markdown, html, docx, pdf", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranslationExport_PostIncompleteTranslation_ReturnsBadRequestWithoutWritingFile()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("disabled-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        LocalSqliteConnectionFactory connectionFactory = new(tempDescriptor.Descriptor.Store);
        SqliteDocumentRepository documentRepository = new(connectionFactory);
        SqliteTranslationRepository translationRepository = new(connectionFactory);
        ImportedDocument document = await CreateIndexedDocumentAsync(documentRepository, tempDescriptor.Root);
        IReadOnlyList<TranslationSourceUnit> units = await translationRepository.BuildSourceUnitsAsync(document.Id);
        StoredTranslation translation = await translationRepository.CreateAsync(
            document.Id,
            "English",
            "llama-test",
            jobId: null,
            units,
            CancellationToken.None);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            $"/api/translations/{translation.Id}/export",
            new TranslationExportRequest("txt"),
            JsonOptions);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Traduzione incompleta", body, StringComparison.Ordinal);
        string exportDirectory = tempDescriptor.Descriptor.StoragePaths.DocumentExportsDirectory;
        Assert.True(!Directory.Exists(exportDirectory) || !Directory.EnumerateFiles(exportDirectory).Any());
    }

    [Fact]
    public async Task TranslationSourceUnits_LabelSupportedMediaShapes()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("translation-label-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        LocalSqliteConnectionFactory connectionFactory = new(tempDescriptor.Descriptor.Store);
        SqliteDocumentRepository documentRepository = new(connectionFactory);
        SqliteTranslationRepository translationRepository = new(connectionFactory);

        ImportedDocument csv = await CreateIndexedDocumentWithPageAsync(documentRepository, tempDescriptor.Root, "data.csv", ".csv", "name,score\nAlice,42");
        ImportedDocument xlsx = await CreateIndexedDocumentWithPageAsync(documentRepository, tempDescriptor.Root, "book.xlsx", ".xlsx", "Foglio: Data\nRiga 2: [A2] Alice | [B2] 42");
        ImportedDocument pptx = await CreateIndexedDocumentWithPageAsync(documentRepository, tempDescriptor.Root, "deck.pptx", ".pptx", "Slide 1\nTextbox 1: Titolo\nNote 1: Nota relatore");
        ImportedDocument image = await CreateIndexedDocumentWithPageAsync(documentRepository, tempDescriptor.Root, "scan.png", ".png", "Prima riga OCR\nSeconda riga OCR");

        IReadOnlyList<TranslationSourceUnit> csvUnits = await translationRepository.BuildSourceUnitsAsync(csv.Id);
        IReadOnlyList<TranslationSourceUnit> xlsxUnits = await translationRepository.BuildSourceUnitsAsync(xlsx.Id);
        IReadOnlyList<TranslationSourceUnit> pptxUnits = await translationRepository.BuildSourceUnitsAsync(pptx.Id);
        IReadOnlyList<TranslationSourceUnit> imageUnits = await translationRepository.BuildSourceUnitsAsync(image.Id);

        Assert.Contains(csvUnits, unit => unit.DisplayLabel == "Riga 2 - Colonna 2" && unit.UnitKind == "table-cell");
        Assert.Contains(xlsxUnits, unit => unit.DisplayLabel == "Data - B2" && unit.UnitKind == "table-cell");
        Assert.Contains(pptxUnits, unit => unit.DisplayLabel == "Slide 1 - Textbox 1" && unit.UnitKind == "textbox");
        Assert.Contains(pptxUnits, unit => unit.DisplayLabel == "Slide 1 - Note 1" && unit.UnitKind == "slide-note");
        Assert.Contains(imageUnits, unit => unit.DisplayLabel == "Immagine - Riga 2" && unit.UnitKind == "ocr-line");
    }

    [Fact]
    public async Task TranslationExport_PostPdf_ExportsPdfFile()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("disabled-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        SeededTranslation seeded = await CreateCompletedTranslationAsync(tempDescriptor, "pdf-source.docx");
        LocalSqliteConnectionFactory connectionFactory = new(tempDescriptor.Descriptor.Store);
        SqliteTranslationRepository translationRepository = new(connectionFactory);
        TranslationExportService service = new(tempDescriptor.Descriptor, translationRepository);

        TranslationExportResponse? exported = await service.ExportAsync(
            seeded.Translation.Id,
            new TranslationExportRequest("pdf"));

        Assert.NotNull(exported);
        Assert.Equal("pdf", exported.Format);
        Assert.StartsWith(
            Path.GetFullPath(tempDescriptor.Descriptor.StoragePaths.DocumentExportsDirectory),
            Path.GetFullPath(exported.OutputPath),
            StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(exported.OutputPath));
    }

    private static async Task<ImportedDocument> CreateIndexedDocumentWithPageAsync(
        SqliteDocumentRepository documents,
        string root,
        string fileName,
        string extension,
        string text)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ImportedDocument document = await documents.CreateAsync(new CreateDocumentRecordRequest(
            Guid.NewGuid().ToString("N"),
            fileName,
            Path.Combine(root, fileName),
            Guid.NewGuid().ToString("N"),
            DocumentFileTypeDetector.DetectMimeType(fileName),
            extension,
            Encoding.UTF8.GetByteCount(text),
            DocumentStatus.Indexed,
            PageCount: 0,
            CurrentJobId: null,
            LastError: null,
            now,
            now));
        await documents.SaveIngestedPageAsync(
            document.Id,
            new IngestedDocumentPage(1, text),
            [new IngestedDocumentChunk(1, 1, 0, text, 1, Guid.NewGuid().ToString("N"))],
            pageCount: 1);
        return document;
    }
}

