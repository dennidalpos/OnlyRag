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
    public async Task TranslationExport_PostPdf_UsesAppTempDirectoryAndCleansIt()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("disabled-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        SeededTranslation seeded = await CreateCompletedTranslationAsync(tempDescriptor, "pdf-source.docx");
        LocalSqliteConnectionFactory connectionFactory = new(tempDescriptor.Descriptor.Store);
        SqliteTranslationRepository translationRepository = new(connectionFactory);
        FakePdfExportConverter converter = new(tempDescriptor.Descriptor.StoragePaths.TempDirectory);
        TranslationExportService service = new(tempDescriptor.Descriptor, translationRepository, converter);

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
        Assert.NotNull(converter.LastSourcePath);
        Assert.StartsWith(
            Path.GetFullPath(Path.Combine(tempDescriptor.Descriptor.StoragePaths.TempDirectory, "translation-export")),
            Path.GetFullPath(converter.LastSourcePath),
            StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateDirectories(
            Path.Combine(tempDescriptor.Descriptor.StoragePaths.TempDirectory, "translation-export")));
    }

    private sealed class FakePdfExportConverter : IPdfExportConverter
    {
        private readonly string tempRoot;

        public FakePdfExportConverter(string tempRoot)
        {
            this.tempRoot = tempRoot;
        }

        public string? LastSourcePath { get; private set; }

        public Task<PdfExportConverterAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PdfExportConverterAvailability(true, "fake-soffice.exe", "Fake converter available.", null));
        }

        public async Task<PdfExportConversionResult> ConvertToPdfAsync(
            PdfExportConversionRequest request,
            CancellationToken cancellationToken = default)
        {
            LastSourcePath = request.SourcePath;
            string directory = Path.Combine(tempRoot, "translation-export", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string pdfPath = Path.Combine(directory, "export.pdf");
            await File.WriteAllBytesAsync(pdfPath, Encoding.ASCII.GetBytes("%PDF-1.4 fake"), cancellationToken);
            return new PdfExportConversionResult(pdfPath, directory);
        }
    }
}

