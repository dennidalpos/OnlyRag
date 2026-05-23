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
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Api.Tests;

public sealed partial class InProcessBackendTests
{
    [Fact]
    public async Task DocumentsImport_CreatesDocumentAndAllowsLookup()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("disabled-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using MultipartFormDataContent content = new();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("api-document")), "files", "Spec.pdf");

        using HttpResponseMessage importResponse = await httpClient.PostAsync("/api/documents/import", content);
        DocumentImportResponse? importPayload = await importResponse.Content.ReadFromJsonAsync<DocumentImportResponse>(JsonOptions);
        ImportedDocument[]? documents = await httpClient.GetFromJsonAsync<ImportedDocument[]>("/api/documents", JsonOptions);
        Assert.NotNull(importPayload);
        long importedId = importPayload.Documents[0].Document.Id;
        ImportedDocument? detail = await httpClient.GetFromJsonAsync<ImportedDocument>($"/api/documents/{importedId}", JsonOptions);

        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        Assert.Single(importPayload.Documents);
        Assert.False(importPayload.Documents[0].Deduplicated);
        Assert.NotNull(documents);
        Assert.Single(documents);
        Assert.NotNull(detail);
        Assert.Equal(DocumentStatus.Queued, detail.Status);
    }

    [Fact]
    public async Task DocumentsImport_DeduplicatesDuplicateFilesInSameBatch()
    {
        LocalJobQueueDescriptor queueDescriptor = new("batch-dedup-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0);
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(queueDescriptor);
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        byte[] bytes = Encoding.UTF8.GetBytes("same-batch-content");
        using MultipartFormDataContent content = new();
        content.Add(new ByteArrayContent(bytes), "files", "One.txt");
        content.Add(new ByteArrayContent(bytes), "files", "Two.txt");

        using HttpResponseMessage importResponse = await httpClient.PostAsync("/api/documents/import", content);
        DocumentImportResponse? importPayload = await importResponse.Content.ReadFromJsonAsync<DocumentImportResponse>(JsonOptions);
        ImportedDocument[]? documents = await httpClient.GetFromJsonAsync<ImportedDocument[]>("/api/documents", JsonOptions);
        SqliteLocalJobQueue queue = new(new LocalSqliteConnectionFactory(tempDescriptor.Descriptor.Store), queueDescriptor);
        IReadOnlyList<LocalJob> jobs = await queue.ListAsync();

        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        Assert.NotNull(importPayload);
        Assert.Equal(2, importPayload.Documents.Count);
        Assert.False(importPayload.Documents[0].Deduplicated);
        Assert.True(importPayload.Documents[1].Deduplicated);
        Assert.Equal(importPayload.Documents[0].Document.Id, importPayload.Documents[1].Document.Id);
        Assert.NotNull(documents);
        Assert.Single(documents);
        Assert.Single(ListOriginalFiles(tempDescriptor));
        Assert.Single(jobs);
    }

    [Fact]
    public async Task DocumentsImport_ReturnsPerFileFailuresWithoutHidingSuccessfulImports()
    {
        LocalJobQueueDescriptor queueDescriptor = new("partial-import-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0);
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(queueDescriptor);
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        using MultipartFormDataContent content = new();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("valid")), "files", "Valid.txt");
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("invalid")), "files", "Unsupported.json");

        using HttpResponseMessage importResponse = await httpClient.PostAsync("/api/documents/import", content);
        DocumentImportResponse? importPayload = await importResponse.Content.ReadFromJsonAsync<DocumentImportResponse>(JsonOptions);
        ImportedDocument[]? documents = await httpClient.GetFromJsonAsync<ImportedDocument[]>("/api/documents", JsonOptions);

        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        Assert.NotNull(importPayload);
        Assert.True(importPayload.HasFailures);
        DocumentImportResult imported = Assert.Single(importPayload.Documents);
        Assert.Equal("Valid.txt", imported.Document.OriginalFileName);
        Assert.Equal(2, importPayload.Results.Count);
        Assert.Contains(importPayload.Results, result => result.Succeeded && result.FileName == "Valid.txt");
        Assert.Contains(importPayload.Results, result =>
            !result.Succeeded
            && result.FileName == "Unsupported.json"
            && result.ErrorCode == "document_import_invalid");
        Assert.NotNull(documents);
        Assert.Single(documents);
    }

    [Fact]
    public async Task DocumentsImport_RejectsFileLargerThanConfiguredLimitAndLeavesNoOriginals()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("upload-file-limit-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions
            {
                DocumentLibraryLimits = CreateTestLimits(maxFileSizeBytes: 5, maxBatchSizeBytes: 100)
            });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        using MultipartFormDataContent content = new();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("123456")), "files", "TooLarge.txt");

        using HttpResponseMessage response = await httpClient.PostAsync("/api/documents/import", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(ListOriginalFiles(tempDescriptor));
    }

    [Fact]
    public async Task DocumentsImport_RejectsInvalidFileNameBeforeSizeLimit()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("upload-file-name-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions
            {
                DocumentLibraryLimits = CreateTestLimits(maxFileSizeBytes: 5, maxBatchSizeBytes: 100)
            });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        using MultipartFormDataContent content = new();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("123456")), "files", "Bad:Name.txt");

        using HttpResponseMessage response = await httpClient.PostAsync("/api/documents/import", content);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("nome file", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("File troppo grande", body, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(ListOriginalFiles(tempDescriptor));
    }

    [Fact]
    public async Task DocumentsImport_RejectsBatchLargerThanConfiguredLimit()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("upload-batch-limit-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions
            {
                DocumentLibraryLimits = CreateTestLimits(maxFileSizeBytes: 100, maxBatchSizeBytes: 10)
            });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        using MultipartFormDataContent content = new();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("123456")), "files", "One.txt");
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("abcdef")), "files", "Two.txt");

        using HttpResponseMessage response = await httpClient.PostAsync("/api/documents/import", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(ListOriginalFiles(tempDescriptor));
    }

    [Fact]
    public async Task DocumentsImport_RejectsTooManyFiles()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("upload-file-count-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions
            {
                DocumentLibraryLimits = CreateTestLimits(maxFilesPerImport: 1)
            });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        using MultipartFormDataContent content = new();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("one")), "files", "One.txt");
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("two")), "files", "Two.txt");

        using HttpResponseMessage response = await httpClient.PostAsync("/api/documents/import", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(ListOriginalFiles(tempDescriptor));
    }

    [Fact]
    public async Task DocumentsImport_RejectsLibraryQuotaOverflow()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("upload-quota-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions
            {
                DocumentLibraryLimits = CreateTestLimits(libraryQuotaBytes: 5)
            });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        using MultipartFormDataContent content = new();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("123456")), "files", "Quota.txt");

        using HttpResponseMessage response = await httpClient.PostAsync("/api/documents/import", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(ListOriginalFiles(tempDescriptor));
    }

    [Fact]
    public async Task DocumentsImport_RejectsLowDiskCondition()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("upload-low-disk-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions
            {
                DocumentLibraryLimits = CreateTestLimits(
                    libraryQuotaBytes: long.MaxValue,
                    minimumFreeDiskBytes: long.MaxValue)
            });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        using MultipartFormDataContent content = new();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("small")), "files", "LowDisk.txt");

        using HttpResponseMessage response = await httpClient.PostAsync("/api/documents/import", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(ListOriginalFiles(tempDescriptor));
    }

    [Fact]
    public async Task DocumentsImport_SerializesOcrLanguageInIngestionJob()
    {
        LocalJobQueueDescriptor queueDescriptor = new("ocr-language-import-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0);
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(queueDescriptor);
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        using MultipartFormDataContent content = new();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("api-document")), "files", "Spec.pdf");
        content.Add(new StringContent("ForceAll"), "ocrPolicy");
        content.Add(new StringContent("fr"), "ocrLanguage");

        using HttpResponseMessage importResponse = await httpClient.PostAsync("/api/documents/import", content);
        SqliteLocalJobQueue queue = new(new LocalSqliteConnectionFactory(tempDescriptor.Descriptor.Store), queueDescriptor);
        IReadOnlyList<LocalJob> jobs = await queue.ListAsync();
        LocalJob job = Assert.Single(jobs);
        DocumentIngestionJobPayload? payload = JsonSerializer.Deserialize<DocumentIngestionJobPayload>(job.PayloadJson, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        Assert.NotNull(payload);
        Assert.True(payload.ForceOcr);
        Assert.Equal("fr", payload.OcrLanguage);
    }

    [Fact]
    public async Task DocumentsImport_UsesSavedOcrLanguageWhenRequestOmitsLanguage()
    {
        LocalJobQueueDescriptor queueDescriptor = new("ocr-language-import-default-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0);
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(queueDescriptor);
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        await httpClient.PutAsJsonAsync(
            "/api/settings/ocr-processing",
            new OcrProcessingSettings("de", 2, 180, 0.55d),
            JsonOptions);
        using MultipartFormDataContent content = new();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("api-document")), "files", "Spec.pdf");
        content.Add(new StringContent("ForceAll"), "ocrPolicy");

        using HttpResponseMessage importResponse = await httpClient.PostAsync("/api/documents/import", content);
        SqliteLocalJobQueue queue = new(new LocalSqliteConnectionFactory(tempDescriptor.Descriptor.Store), queueDescriptor);
        LocalJob job = Assert.Single(await queue.ListAsync());
        DocumentIngestionJobPayload? payload = JsonSerializer.Deserialize<DocumentIngestionJobPayload>(job.PayloadJson, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("de", payload.OcrLanguage);
    }

    [Fact]
    public async Task DocumentsOcr_SerializesOcrLanguageInIngestionJob()
    {
        LocalJobQueueDescriptor queueDescriptor = new("ocr-language-force-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0);
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(queueDescriptor);
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        LocalSqliteConnectionFactory connectionFactory = new(tempDescriptor.Descriptor.Store);
        SqliteDocumentRepository documentRepository = new(connectionFactory);
        ImportedDocument document = await documentRepository.CreateAsync(new CreateDocumentRecordRequest(
            Guid.NewGuid().ToString("N"),
            "scan.pdf",
            Path.Combine(tempDescriptor.Root, "scan.pdf"),
            "sha-ocr-language",
            "application/pdf",
            ".pdf",
            128,
            DocumentStatus.Indexed,
            PageCount: 0,
            CurrentJobId: null,
            LastError: null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsync(
            $"/api/documents/{document.Id}/ocr?force=true&ocrLanguage=de",
            content: null);
        SqliteLocalJobQueue queue = new(connectionFactory, queueDescriptor);
        IReadOnlyList<LocalJob> jobs = await queue.ListAsync();
        LocalJob job = Assert.Single(jobs);
        DocumentIngestionJobPayload? payload = JsonSerializer.Deserialize<DocumentIngestionJobPayload>(job.PayloadJson, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.True(payload.ForceOcr);
        Assert.Equal("de", payload.OcrLanguage);
    }

    [Fact]
    public async Task DocumentsOcr_UsesSavedOcrLanguageWhenRequestOmitsLanguage()
    {
        LocalJobQueueDescriptor queueDescriptor = new("ocr-language-force-default-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0);
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(queueDescriptor);
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        LocalSqliteConnectionFactory connectionFactory = new(tempDescriptor.Descriptor.Store);
        SqliteDocumentRepository documentRepository = new(connectionFactory);
        ImportedDocument document = await documentRepository.CreateAsync(new CreateDocumentRecordRequest(
            Guid.NewGuid().ToString("N"),
            "scan.pdf",
            Path.Combine(tempDescriptor.Root, "scan.pdf"),
            "sha-ocr-language-default",
            "application/pdf",
            ".pdf",
            128,
            DocumentStatus.Indexed,
            PageCount: 0,
            CurrentJobId: null,
            LastError: null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        await httpClient.PutAsJsonAsync(
            "/api/settings/ocr-processing",
            new OcrProcessingSettings("fr", 2, 180, 0.55d),
            JsonOptions);

        using HttpResponseMessage response = await httpClient.PostAsync(
            $"/api/documents/{document.Id}/ocr?force=true",
            content: null);
        SqliteLocalJobQueue queue = new(connectionFactory, queueDescriptor);
        LocalJob job = Assert.Single(await queue.ListAsync());
        DocumentIngestionJobPayload? payload = JsonSerializer.Deserialize<DocumentIngestionJobPayload>(job.PayloadJson, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("fr", payload.OcrLanguage);
    }

    [Fact]
    public async Task DocumentsDelete_RemovesDocumentAndDocumentsReindex_QueuesFreshJob()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("disabled-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        async Task<ImportedDocument> ImportAsync(string fileName, string contentValue)
        {
            using MultipartFormDataContent content = new();
            content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(contentValue)), "files", fileName);
            using HttpResponseMessage importResponse = await httpClient.PostAsync("/api/documents/import", content);
            DocumentImportResponse? payload = await importResponse.Content.ReadFromJsonAsync<DocumentImportResponse>(JsonOptions);
            Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
            Assert.NotNull(payload);
            return payload.Documents[0].Document;
        }

        ImportedDocument imported = await ImportAsync("DeleteMe.pdf", "delete-me");

        using HttpResponseMessage reindexResponse = await httpClient.PostAsync($"/api/documents/{imported.Id}/reindex", content: null);
        ImportedDocument? reindexed = await reindexResponse.Content.ReadFromJsonAsync<ImportedDocument>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, reindexResponse.StatusCode);
        Assert.NotNull(reindexed);
        Assert.NotEqual(imported.CurrentJobId, reindexed.CurrentJobId);
        Assert.Equal(DocumentStatus.Queued, reindexed.Status);

        using HttpResponseMessage deleteResponse = await httpClient.DeleteAsync($"/api/documents/{imported.Id}");
        using HttpResponseMessage detailAfterDelete = await httpClient.GetAsync($"/api/documents/{imported.Id}");

        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, detailAfterDelete.StatusCode);
    }

    [Fact]
    public async Task DocumentPreview_ReturnsRequestedPageWindow()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("disabled-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        LocalSqliteConnectionFactory connectionFactory = new(tempDescriptor.Descriptor.Store);
        SqliteDocumentRepository documentRepository = new(connectionFactory);
        ImportedDocument document = await CreateIndexedDocumentAsync(documentRepository, tempDescriptor.Root);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        DocumentPreviewResponse? preview = await httpClient.GetFromJsonAsync<DocumentPreviewResponse>(
            $"/api/documents/{document.Id}/preview?page=2&pageSize=1",
            JsonOptions);

        Assert.NotNull(preview);
        Assert.Equal(2, preview.PageCount);
        Assert.Equal(2, preview.PageStart);
        Assert.Equal(1, preview.PageSize);
        Assert.Equal(1, preview.ReturnedPageCount);
        DocumentPageInfo page = Assert.Single(preview.Pages);
        Assert.Equal(2, page.PageNumber);
        Assert.Equal("Pagina due", page.TextContent);
    }

    [Fact]
    public async Task DocumentPreview_ClampsOutOfRangePageToLastKnownPage()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("disabled-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        LocalSqliteConnectionFactory connectionFactory = new(tempDescriptor.Descriptor.Store);
        SqliteDocumentRepository documentRepository = new(connectionFactory);
        ImportedDocument document = await CreateIndexedDocumentAsync(documentRepository, tempDescriptor.Root);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        DocumentPreviewResponse? preview = await httpClient.GetFromJsonAsync<DocumentPreviewResponse>(
            $"/api/documents/{document.Id}/preview?page=999&pageSize=50",
            JsonOptions);

        Assert.NotNull(preview);
        Assert.Equal(2, preview.PageStart);
        Assert.Equal(10, preview.PageSize);
        DocumentPageInfo page = Assert.Single(preview.Pages);
        Assert.Equal(2, page.PageNumber);
    }
}

