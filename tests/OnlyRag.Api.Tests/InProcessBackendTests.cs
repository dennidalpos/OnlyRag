using System.Net;
using System.Net.Http.Json;
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

public sealed class InProcessBackendTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    [Fact]
    public async Task Health_ReturnsHealthyFromLoopbackDynamicPort()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);

        Assert.True(IPAddress.IsLoopback(IPAddress.Parse(backend.BaseUri.Host)));
        Assert.NotEqual(0, backend.BaseUri.Port);

        using HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };

        BackendHealthResponse? response = await httpClient.GetFromJsonAsync<BackendHealthResponse>("/health");

        Assert.NotNull(response);
        Assert.Equal("Healthy", response.Status);
    }

    [Fact]
    public async Task AppStatus_AllowsStaticWebViewOrigin()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };

        using HttpRequestMessage request = new(HttpMethod.Get, "/api/app/status");
        request.Headers.Add("Origin", OnlyRagWebOrigins.StaticWebViewOrigin);

        using HttpResponseMessage response = await httpClient.SendAsync(request);
        AppStatusResponse? status = await response.Content.ReadFromJsonAsync<AppStatusResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OnlyRagWebOrigins.StaticWebViewOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.NotNull(status);
        Assert.Equal("Running", status.Backend);
    }

    [Fact]
    public async Task StorageStatus_ReturnsDatabasePathAndMigrationState()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };

        StorageStatusResponse? status = await httpClient.GetFromJsonAsync<StorageStatusResponse>("/api/app/storage-status");

        Assert.NotNull(status);
        Assert.Equal(tempDescriptor.Descriptor.StoragePaths.DatabasePath, status.DatabasePath);
        Assert.Equal("Current", status.MigrationStatus);
        Assert.Equal(status.TargetSchemaVersion, status.CurrentSchemaVersion);
    }

    [Fact]
    public async Task Jobs_ReturnsPersistentQueue()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };

        LocalJob[]? jobs = await httpClient.GetFromJsonAsync<LocalJob[]>("/api/jobs");

        Assert.NotNull(jobs);
        Assert.Empty(jobs);
    }

    [Fact]
    public async Task PrepareShutdown_WithNoActiveJobs_ReturnsCompleteResponse()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("shutdown-empty-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };

        using HttpResponseMessage response = await httpClient.PostAsync("/api/app/prepare-shutdown", content: null);
        AppShutdownPreparationResponse? payload = await response.Content.ReadFromJsonAsync<AppShutdownPreparationResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.True(payload.IsComplete);
        Assert.Equal(0, payload.ActiveJobCount);
        Assert.Equal(0, payload.CancelledJobCount);
        Assert.Empty(payload.UnstoppedJobIds);
    }

    [Fact]
    public async Task PrepareShutdown_CancelsPendingPausedAndRunningJobs()
    {
        LocalJobQueueDescriptor queueDescriptor = new("shutdown-active-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0);
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(queueDescriptor);
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        SqliteLocalJobQueue queue = new(new LocalSqliteConnectionFactory(tempDescriptor.Descriptor.Store), queueDescriptor);
        LocalJob runningSeed = await queue.CreateAsync(new CreateLocalJobRequest("running-test", "{}"));
        LocalJob? running = await queue.TryLeaseNextAsync();
        Assert.NotNull(running);
        Assert.Equal(runningSeed.Id, running.Id);
        LocalJob pending = await queue.CreateAsync(new CreateLocalJobRequest("pending-test", "{}"));
        LocalJob paused = await queue.CreateAsync(new CreateLocalJobRequest("paused-test", "{}"));
        await queue.PauseAsync(paused.Id);
        using HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };

        using HttpResponseMessage response = await httpClient.PostAsync("/api/app/prepare-shutdown", content: null);
        AppShutdownPreparationResponse? payload = await response.Content.ReadFromJsonAsync<AppShutdownPreparationResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.True(payload.IsComplete);
        Assert.Equal(3, payload.ActiveJobCount);
        Assert.Equal(3, payload.CancelledJobCount);
        Assert.Equal(JobStatus.Cancelled, (await queue.GetAsync(pending.Id))!.Status);
        Assert.Equal(JobStatus.Cancelled, (await queue.GetAsync(paused.Id))!.Status);
        Assert.Equal(JobStatus.Cancelled, (await queue.GetAsync(running.Id))!.Status);
    }

    [Fact]
    public async Task PrepareShutdown_WaitsForRegisteredRunningJobToStop()
    {
        LocalJobQueueDescriptor queueDescriptor = new("shutdown-registry-stop-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0);
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(queueDescriptor);
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        SqliteLocalJobQueue queue = new(new LocalSqliteConnectionFactory(tempDescriptor.Descriptor.Store), queueDescriptor);
        LocalJob created = await queue.CreateAsync(new CreateLocalJobRequest("running-test", "{}"));
        LocalJob running = (await queue.TryLeaseNextAsync())!;
        RunningJobCancellationRegistry registry = new();
        CancellationTokenSource jobCancellation = registry.Register(created.Id, CancellationToken.None);
        ApplicationShutdownService shutdown = new(queue, registry, tempDescriptor.Descriptor);
        Task unregisterOnCancel = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, jobCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                registry.Unregister(created.Id);
            }
        });

        AppShutdownPreparationResponse payload = await shutdown.PrepareAsync(TimeSpan.FromSeconds(5));
        await unregisterOnCancel;

        Assert.Equal(running.Id, created.Id);
        Assert.True(payload.IsComplete);
        Assert.Empty(payload.UnstoppedJobIds);
        Assert.Equal(JobStatus.Cancelled, (await queue.GetAsync(created.Id))!.Status);
    }

    [Fact]
    public async Task PrepareShutdown_ReturnsUnstoppedRegisteredRunningJobsAfterTimeout()
    {
        LocalJobQueueDescriptor queueDescriptor = new("shutdown-registry-timeout-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0);
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(queueDescriptor);
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        SqliteLocalJobQueue queue = new(new LocalSqliteConnectionFactory(tempDescriptor.Descriptor.Store), queueDescriptor);
        LocalJob created = await queue.CreateAsync(new CreateLocalJobRequest("running-test", "{}"));
        LocalJob running = (await queue.TryLeaseNextAsync())!;
        RunningJobCancellationRegistry registry = new();
        registry.Register(created.Id, CancellationToken.None);
        ApplicationShutdownService shutdown = new(queue, registry, tempDescriptor.Descriptor);

        AppShutdownPreparationResponse payload = await shutdown.PrepareAsync(TimeSpan.FromMilliseconds(120));
        registry.Unregister(created.Id);

        Assert.Equal(running.Id, created.Id);
        Assert.False(payload.IsComplete);
        Assert.Equal([created.Id], payload.UnstoppedJobIds);
        Assert.Equal(JobStatus.Cancelled, (await queue.GetAsync(created.Id))!.Status);
    }

    [Fact]
    public async Task DocumentsImport_CreatesDocumentAndAllowsLookup()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("disabled-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };

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
    public async Task DocumentsImport_SerializesOcrLanguageInIngestionJob()
    {
        LocalJobQueueDescriptor queueDescriptor = new("ocr-language-import-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0);
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(queueDescriptor);
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };
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
        using HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };

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
    public async Task DocumentsDelete_RemovesDocumentAndDocumentsReindex_QueuesFreshJob()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("disabled-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };

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
    public async Task OllamaSettings_CanBeSavedAndReadBack()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };

        OllamaSettings request = new(
            "http://192.168.1.40:11434",
            "chat-model",
            "embedding-model",
            "translation-model",
            180,
            2);

        using HttpResponseMessage putResponse = await httpClient.PutAsJsonAsync("/api/settings/ollama", request);
        OllamaSettings? saved = await putResponse.Content.ReadFromJsonAsync<OllamaSettings>();
        OllamaSettings? current = await httpClient.GetFromJsonAsync<OllamaSettings>("/api/settings/ollama");

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        Assert.NotNull(saved);
        Assert.NotNull(current);
        Assert.Equal(request, saved);
        Assert.Equal(request, current);
    }

    [Fact]
    public async Task PerformanceSettings_CanBeSavedAndReadBackWithLowResourceLimits()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };

        PerformanceSettings request = new(
            MaxParallelJobs: 4,
            MaxOcrParallelPages: 4,
            EmbeddingBatchSize: 8,
            TranslationBatchSize: 4,
            MaxContextChunks: 16,
            RequestTimeoutSeconds: 180,
            EnableLowResourceMode: true);

        using HttpResponseMessage putResponse = await httpClient.PutAsJsonAsync("/api/settings/performance", request);
        PerformanceSettings? saved = await putResponse.Content.ReadFromJsonAsync<PerformanceSettings>();
        PerformanceSettings? current = await httpClient.GetFromJsonAsync<PerformanceSettings>("/api/settings/performance");

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        Assert.NotNull(saved);
        Assert.NotNull(current);
        Assert.True(current.EnableLowResourceMode);
        Assert.Equal(1, current.MaxParallelJobs);
        Assert.Equal(1, current.MaxOcrParallelPages);
        Assert.Equal(1, current.EmbeddingBatchSize);
        Assert.Equal(1, current.TranslationBatchSize);
        Assert.Equal(8, current.MaxContextChunks);
        Assert.Equal(180, current.RequestTimeoutSeconds);
    }

    [Fact]
    public async Task OcrSettings_CanBeSavedAndReadBackNormalized()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };

        OcrSettings request = new(
            Profile: "accurate",
            PdfDpi: 300,
            ModelPreset: "PP-OCRv5",
            ModelVersion: "PP-OCRv5",
            DetectionSideLimit: 1280,
            DetectionThreshold: 0.25d,
            DetectionBoxThreshold: 0.55d,
            DetectionUnclipRatio: 1.7d,
            RecognitionScoreThreshold: 0.45d,
            UseTextlineOrientation: false,
            UseDocumentOrientationClassification: true,
            UseDocumentUnwarping: true,
            RecognitionBatchSize: 12,
            CpuThreads: 4,
            Device: "cpu");

        using HttpResponseMessage putResponse = await httpClient.PutAsJsonAsync("/api/settings/ocr", request);
        OcrSettings? saved = await putResponse.Content.ReadFromJsonAsync<OcrSettings>();
        OcrSettings? current = await httpClient.GetFromJsonAsync<OcrSettings>("/api/settings/ocr");

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        Assert.NotNull(saved);
        Assert.NotNull(current);
        Assert.Equal(request, saved);
        Assert.Equal(request, current);
    }

    [Fact]
    public async Task OfficeConversionSettings_InvalidPathReturnsRequiresAdditionalComponentStatus()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };

        OfficeConversionSettings request = new(
            @"C:\OnlyRag\Tests\MissingLibreOffice\soffice.exe",
            30);

        using HttpResponseMessage putResponse = await httpClient.PutAsJsonAsync("/api/settings/office-conversion", request);
        OfficeConversionSettings? saved = await putResponse.Content.ReadFromJsonAsync<OfficeConversionSettings>();
        OfficeConverterStatusResponse? status = await httpClient.GetFromJsonAsync<OfficeConverterStatusResponse>("/api/office-converter/status");

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        Assert.NotNull(saved);
        Assert.Equal(request, saved);
        Assert.NotNull(status);
        Assert.False(status.IsAvailable);
        Assert.Equal("RequiresAdditionalComponent", status.State);
        Assert.Equal(30, status.ConversionTimeoutSeconds);
    }

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
        using HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };

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
        using HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };

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
        using HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };

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
        using HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };

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
        using HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };

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
        using HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            $"/api/translations/{seeded.Translation.Id}/export",
            new TranslationExportRequest("rtf"),
            JsonOptions);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("txt, markdown, html, docx, pdf", body, StringComparison.Ordinal);
    }

    private static async Task<ImportedDocument> CreateIndexedDocumentAsync(
        SqliteDocumentRepository documents,
        string root)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ImportedDocument document = await documents.CreateAsync(new CreateDocumentRecordRequest(
            "doc-compare-1",
            "compare.txt",
            Path.Combine(root, "compare.txt"),
            "sha-compare",
            "text/plain",
            ".txt",
            64,
            DocumentStatus.Indexed,
            PageCount: 0,
            CurrentJobId: null,
            LastError: null,
            now,
            now));

        await documents.SaveIngestedPageAsync(
            document.Id,
            new IngestedDocumentPage(1, "Pagina uno"),
            [
                new IngestedDocumentChunk(1, 1, 0, "Pagina uno", 2, "hash-compare-a")
            ],
            pageCount: 1);
        await documents.SaveIngestedPageAsync(
            document.Id,
            new IngestedDocumentPage(2, "Pagina due"),
            [
                new IngestedDocumentChunk(2, 2, 1, "Pagina due", 2, "hash-compare-b")
            ],
            pageCount: 2);

        return document;
    }

    private static async Task<SeededTranslation> CreateCompletedTranslationAsync(
        TempBackendDescriptor tempDescriptor,
        string documentName)
    {
        LocalSqliteConnectionFactory connectionFactory = new(tempDescriptor.Descriptor.Store);
        SqliteDocumentRepository documentRepository = new(connectionFactory);
        SqliteTranslationRepository translationRepository = new(connectionFactory);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ImportedDocument document = await documentRepository.CreateAsync(new CreateDocumentRecordRequest(
            Guid.NewGuid().ToString("N"),
            documentName,
            Path.Combine(tempDescriptor.Root, "source.txt"),
            "sha-export",
            "text/plain",
            Path.GetExtension(documentName),
            128,
            DocumentStatus.Indexed,
            PageCount: 0,
            CurrentJobId: null,
            LastError: null,
            now,
            now));

        await documentRepository.SaveIngestedPageAsync(
            document.Id,
            new IngestedDocumentPage(1, "Primo paragrafo\n\nSecondo paragrafo"),
            [
                new IngestedDocumentChunk(1, 1, 0, "Primo paragrafo", 2, "hash-export-a"),
                new IngestedDocumentChunk(1, 1, 1, "Secondo paragrafo", 2, "hash-export-b")
            ],
            pageCount: 1);
        await documentRepository.SaveIngestedPageAsync(
            document.Id,
            new IngestedDocumentPage(2, "Riga 1: Cella 1: Nome | Cella 2: Valore"),
            [
                new IngestedDocumentChunk(2, 2, 2, "Riga 1: Cella 1: Nome | Cella 2: Valore", 6, "hash-export-c")
            ],
            pageCount: 2);

        IReadOnlyList<TranslationSourceUnit> units = await translationRepository.BuildSourceUnitsAsync(document.Id);
        StoredTranslation translation = await translationRepository.CreateAsync(
            document.Id,
            "English",
            "llama-test",
            jobId: null,
            units,
            CancellationToken.None);
        IReadOnlyList<StoredTranslationUnit> storedUnits = await translationRepository.ListUnitsAsync(translation.Id);
        string[] translatedTexts = ["Prima traduzione", "Seconda traduzione", "Nome tradotto", "Valore tradotto"];
        for (int index = 0; index < storedUnits.Count; index++)
        {
            await translationRepository.SaveUnitSuccessAsync(
                storedUnits[index].Id,
                translatedTexts[Math.Min(index, translatedTexts.Length - 1)],
                validationWarnings: null);
        }

        await translationRepository.RefreshProgressAsync(translation.Id, "Completed", null);
        return new SeededTranslation((await translationRepository.GetAsync(translation.Id))!, storedUnits);
    }

    private sealed record SeededTranslation(
        StoredTranslation Translation,
        IReadOnlyList<StoredTranslationUnit> Units);

    private sealed class TempBackendDescriptor : IDisposable
    {
        private TempBackendDescriptor(string root, LocalJobQueueDescriptor? jobQueue)
        {
            Root = root;
            AppStoragePaths paths = AppStoragePaths.FromRoot(root);
            Descriptor = new InProcessBackendDescriptor(
                paths,
                new LocalSqliteStoreDescriptor(paths),
                jobQueue ?? LocalJobQueueDescriptor.Default,
                new OllamaEndpointOptions());
        }

        public string Root { get; }

        public InProcessBackendDescriptor Descriptor { get; }

        public static TempBackendDescriptor Create(LocalJobQueueDescriptor? jobQueue = null)
        {
            string root = Path.Combine(Path.GetTempPath(), "OnlyRag.Api.Tests", Guid.NewGuid().ToString("N"));
            return new TempBackendDescriptor(root, jobQueue);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
