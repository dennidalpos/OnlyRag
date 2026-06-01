using System.Text.Json;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Api.Tests;

public sealed class DocumentTranslationJobHandlerTests
{
    [Fact]
    public void BuildMessages_IncludesTranslationConstraints()
    {
        StoredTranslationUnit unit = new(
            10,
            1,
            0,
            "paragraph",
            5,
            2,
            "Totale {amount}: 123,45 EUR",
            "hash",
            "{}",
            null,
            null,
            "Pending",
            false,
            null,
            null,
            0,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        IReadOnlyList<OllamaChatMessage> messages = DocumentTranslationPromptBuilder.BuildMessages("English", unit);

        Assert.Equal(2, messages.Count);
        Assert.Contains("Translate only the text inside", messages[0].Content, StringComparison.Ordinal);
        Assert.Contains("Preserve numbers, dates, codes, placeholders", messages[0].Content, StringComparison.Ordinal);
        Assert.Contains("Totale {amount}: 123,45 EUR", messages[1].Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_FailsWhenPlaceholderChanges()
    {
        TranslationValidationResult result = TranslationOutputValidator.Validate(
            "Ordine {orderId} del 2026-04-25: 123,45 EUR",
            "Order {order} on 25/04/2026: EUR");

        Assert.False(result.IsValid);
        Assert.Contains("{orderId}", result.Warnings, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_SucceedsWhenNumbersChangeButPlaceholdersPreserved()
    {
        TranslationValidationResult result = TranslationOutputValidator.Validate(
            "Total: 184783 items on 2024-01-15",
            "Totale: 184.783 articoli il 15/01/2024");

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ExecuteAsync_TranslatesUnitsAndCheckpointsAfterEachUnit()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();
        SqliteDocumentRepository documentRepository = new(tempStorage.CreateConnectionFactory());
        SqliteTranslationRepository translationRepository = new(tempStorage.CreateConnectionFactory());
        SqliteLocalJobQueue queue = tempStorage.CreateQueue();
        ImportedDocument document = await CreateIndexedDocumentAsync(documentRepository, tempStorage.Root);
        IReadOnlyList<TranslationSourceUnit> units = await translationRepository.BuildSourceUnitsAsync(document.Id);
        StoredTranslation translation = await translationRepository.CreateAsync(
            document.Id,
            "English",
            "llama-test",
            jobId: null,
            units,
            CancellationToken.None);
        LocalJob created = await queue.CreateAsync(new CreateLocalJobRequest(
            DocumentTranslationJobHandler.DocumentTranslationJobType,
            JsonSerializer.Serialize(new DocumentTranslationJobPayload(
                translation.Id,
                document.Id,
                "English",
                "llama-test")),
            MaxRetries: 2));
        await translationRepository.UpdateTranslationJobAsync(translation.Id, created.Id, "Queued", null);
        LocalJob leased = (await queue.TryLeaseNextAsync())!;
        EchoTranslationClient ollamaClient = new();
        StubPerformanceSettingsService performanceSettings = new(new PerformanceSettings(
            1,
            1,
            1,
            2,
            8,
            60,
            false));
        DocumentTranslationJobHandler handler = new(
            translationRepository,
            ollamaClient,
            performanceSettings,
            new StubOllamaSettingsService());

        await handler.ExecuteAsync(leased, queue, CancellationToken.None);

        LocalJob stored = (await queue.GetAsync(created.Id))!;
        StoredTranslation completed = (await translationRepository.GetAsync(translation.Id))!;
        IReadOnlyList<StoredTranslationUnit> completedUnits = await translationRepository.ListUnitsAsync(translation.Id);

        Assert.Equal(JobStatus.Running, stored.Status);
        Assert.Equal(100, stored.ProgressPercent);
        Assert.Contains("\"Mode\":\"completed\"", stored.CheckpointJson, StringComparison.Ordinal);
        Assert.Equal("Completed", completed.Status);
        Assert.Equal(2, completed.CompletedUnitCount);
        Assert.Equal(2, ollamaClient.CallCount);
        Assert.Equal(1, performanceSettings.GetCallCount);
        Assert.All(completedUnits, unit => Assert.Equal("Completed", unit.Status));
        Assert.All(completedUnits, unit => Assert.NotNull(unit.TranslatedText));
        Assert.All(completedUnits, unit => Assert.Equal(unit.MachineTranslatedText, unit.TranslatedText));
        Assert.All(completedUnits, unit => Assert.False(unit.ManuallyEdited));
    }

    [Fact]
    public async Task ExecuteAsync_RepairsMissingPlaceholderBeforeSavingUnit()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();
        SqliteDocumentRepository documentRepository = new(tempStorage.CreateConnectionFactory());
        SqliteTranslationRepository translationRepository = new(tempStorage.CreateConnectionFactory());
        SqliteLocalJobQueue queue = tempStorage.CreateQueue();
        ImportedDocument document = await CreateIndexedDocumentAsync(documentRepository, tempStorage.Root);
        StoredTranslation translation = await translationRepository.CreateAsync(
            document.Id,
            "English",
            "llama-test",
            jobId: null,
            await translationRepository.BuildSourceUnitsAsync(document.Id),
            CancellationToken.None);
        LocalJob created = await queue.CreateAsync(new CreateLocalJobRequest(
            DocumentTranslationJobHandler.DocumentTranslationJobType,
            JsonSerializer.Serialize(new DocumentTranslationJobPayload(
                translation.Id,
                document.Id,
                "English",
                "llama-test"))));
        LocalJob leased = (await queue.TryLeaseNextAsync())!;
        RepairingTranslationClient ollamaClient = new();
        DocumentTranslationJobHandler handler = new(
            translationRepository,
            ollamaClient,
            new StubPerformanceSettingsService(new PerformanceSettings(1, 1, 1, 1, 8, 60, false)),
            new StubOllamaSettingsService());

        await handler.ExecuteAsync(leased, queue, CancellationToken.None);

        IReadOnlyList<StoredTranslationUnit> translatedUnits = await translationRepository.ListUnitsAsync(translation.Id);

        Assert.Equal(3, ollamaClient.CallCount);
        Assert.All(translatedUnits, unit => Assert.Equal("Completed", unit.Status));
        Assert.Contains("{name}", translatedUnits[0].TranslatedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_RetriesTimeoutBeforeSavingUnit()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();
        SqliteDocumentRepository documentRepository = new(tempStorage.CreateConnectionFactory());
        SqliteTranslationRepository translationRepository = new(tempStorage.CreateConnectionFactory());
        SqliteLocalJobQueue queue = tempStorage.CreateQueue();
        ImportedDocument document = await CreateIndexedDocumentAsync(documentRepository, tempStorage.Root);
        StoredTranslation translation = await translationRepository.CreateAsync(
            document.Id,
            "English",
            "llama-test",
            jobId: null,
            await translationRepository.BuildSourceUnitsAsync(document.Id),
            CancellationToken.None);
        LocalJob created = await queue.CreateAsync(new CreateLocalJobRequest(
            DocumentTranslationJobHandler.DocumentTranslationJobType,
            JsonSerializer.Serialize(new DocumentTranslationJobPayload(
                translation.Id,
                document.Id,
                "English",
                "llama-test"))));
        LocalJob leased = (await queue.TryLeaseNextAsync())!;
        TimeoutThenSuccessTranslationClient ollamaClient = new();
        DocumentTranslationJobHandler handler = new(
            translationRepository,
            ollamaClient,
            new StubPerformanceSettingsService(new PerformanceSettings(1, 1, 1, 1, 8, 60, false)),
            new StubOllamaSettingsService());

        await handler.ExecuteAsync(leased, queue, CancellationToken.None);

        LocalJob stored = (await queue.GetAsync(created.Id))!;
        StoredTranslation completed = (await translationRepository.GetAsync(translation.Id))!;

        Assert.Equal(JobStatus.Running, stored.Status);
        Assert.Equal("Completed", completed.Status);
        Assert.Equal(3, ollamaClient.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_FailsValidationWithoutGlobalRetryAfterLocalExhaustion()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();
        SqliteDocumentRepository documentRepository = new(tempStorage.CreateConnectionFactory());
        SqliteTranslationRepository translationRepository = new(tempStorage.CreateConnectionFactory());
        SqliteLocalJobQueue queue = tempStorage.CreateQueue();
        ImportedDocument document = await CreateIndexedDocumentAsync(documentRepository, tempStorage.Root);
        StoredTranslation translation = await translationRepository.CreateAsync(
            document.Id,
            "English",
            "llama-test",
            jobId: null,
            await translationRepository.BuildSourceUnitsAsync(document.Id),
            CancellationToken.None);
        LocalJob created = await queue.CreateAsync(new CreateLocalJobRequest(
            DocumentTranslationJobHandler.DocumentTranslationJobType,
            JsonSerializer.Serialize(new DocumentTranslationJobPayload(
                translation.Id,
                document.Id,
                "English",
                "llama-test"))));
        LocalJob leased = (await queue.TryLeaseNextAsync())!;
        AlwaysInvalidPlaceholderTranslationClient ollamaClient = new();
        DocumentTranslationJobHandler handler = new(
            translationRepository,
            ollamaClient,
            new StubPerformanceSettingsService(new PerformanceSettings(1, 1, 1, 1, 8, 60, false)),
            new StubOllamaSettingsService());

        await handler.ExecuteAsync(leased, queue, CancellationToken.None);

        LocalJob stored = (await queue.GetAsync(created.Id))!;
        StoredTranslation failed = (await translationRepository.GetAsync(translation.Id))!;
        IReadOnlyList<StoredTranslationUnit> units = await translationRepository.ListUnitsAsync(translation.Id);

        Assert.Equal(JobStatus.Failed, stored.Status);
        Assert.Null(stored.NextAttemptAtUtc);
        Assert.Equal("Failed", failed.Status);
        Assert.Equal("Failed", units[0].Status);
        Assert.Contains("testo sorgente originale", units[0].Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4, ollamaClient.CallCount);
    }

    [Fact]
    public async Task Repository_UpdateUnitText_SavesManualCorrection()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();
        SqliteDocumentRepository documentRepository = new(tempStorage.CreateConnectionFactory());
        SqliteTranslationRepository translationRepository = new(tempStorage.CreateConnectionFactory());
        ImportedDocument document = await CreateIndexedDocumentAsync(documentRepository, tempStorage.Root);
        IReadOnlyList<TranslationSourceUnit> units = await translationRepository.BuildSourceUnitsAsync(document.Id);
        StoredTranslation translation = await translationRepository.CreateAsync(
            document.Id,
            "English",
            "llama-test",
            jobId: null,
            units,
            CancellationToken.None);
        StoredTranslationUnit firstUnit = (await translationRepository.ListUnitsAsync(translation.Id))[0];
        await translationRepository.SaveUnitSuccessAsync(firstUnit.Id, "Machine translation 123 {name}", null);

        StoredTranslationUnit? corrected = await translationRepository.UpdateUnitTextAsync(
            translation.Id,
            firstUnit.Id,
            "Manual correction 123 {name}");

        Assert.NotNull(corrected);
        Assert.Equal("Corrected", corrected.Status);
        Assert.Equal("Manual correction 123 {name}", corrected.TranslatedText);
        Assert.Equal("Machine translation 123 {name}", corrected.MachineTranslatedText);
        Assert.True(corrected.ManuallyEdited);

        StoredTranslation refreshed = (await translationRepository.GetAsync(translation.Id))!;
        Assert.Equal(1, refreshed.CompletedUnitCount);
        Assert.NotEqual("Completed", refreshed.Status);
    }

    private static async Task<ImportedDocument> CreateIndexedDocumentAsync(
        SqliteDocumentRepository documents,
        string root)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ImportedDocument document = await documents.CreateAsync(new CreateDocumentRecordRequest(
            "doc-translation-1",
            "sample.txt",
            Path.Combine(root, "sample.txt"),
            "sha-translation",
            "text/plain",
            ".txt",
            48,
            DocumentStatus.Indexed,
            PageCount: 0,
            CurrentJobId: null,
            LastError: null,
            now,
            now));

        await documents.SaveIngestedPageAsync(
            document.Id,
            new IngestedDocumentPage(1, "Ciao {name} 123\n\nSeconda riga 456"),
            [
                new IngestedDocumentChunk(1, 1, 0, "Ciao {name} 123", 3, "hash-a"),
                new IngestedDocumentChunk(1, 1, 1, "Seconda riga 456", 3, "hash-b")
            ],
            pageCount: 1);

        return document;
    }

    private class EchoTranslationClient : IOllamaClient
    {
        public int CallCount { get; private set; }

        public Task TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OllamaModelSummary>> ListModelsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<OllamaModelSummary>>(
                [new OllamaModelSummary("llama-test", "llama-test", null, 0, null, null, null, null)]);
        }

        public Task PullModelAsync(string modelName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task PullModelAsync(
            string modelName,
            Func<OllamaModelPullProgress, CancellationToken, Task> onProgress,
            CancellationToken cancellationToken = default)
        {
            return onProgress(new OllamaModelPullProgress("success", null, null, 100), cancellationToken);
        }

        public Task DeleteModelAsync(string modelName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ChatSmokeAsync(string modelName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task EmbeddingsSmokeAsync(string modelName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public virtual Task<string> GenerateChatAsync(
            string modelName,
            IReadOnlyList<OllamaChatMessage> messages,
            int? numCtx = null,
            CancellationToken cancellationToken = default)
        {
            IncrementCallCount();
            string source = ExtractSource(messages[^1].Content);
            return Task.FromResult($"Translated: {source}");
        }

        public Task<OllamaModelDetails> ShowModelAsync(string modelName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new OllamaModelDetails(modelName, null));
        }

        public Task<IReadOnlyList<IReadOnlyList<float>>> GenerateEmbeddingsAsync(
            string modelName,
            IReadOnlyList<string> inputs,
            int? numCtx = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        protected void IncrementCallCount()
        {
            CallCount++;
        }

        protected static string ExtractSource(string prompt)
        {
            const string startMarker = "<<<ONLYRAG_TRANSLATION_UNIT";
            const string endMarker = "ONLYRAG_TRANSLATION_UNIT";
            int start = prompt.IndexOf(startMarker, StringComparison.Ordinal);
            if (start < 0)
            {
                return prompt;
            }

            start = prompt.IndexOf('\n', start);
            int end = prompt.LastIndexOf(endMarker, StringComparison.Ordinal);
            if (start < 0 || end <= start)
            {
                return prompt;
            }

            return prompt[(start + 1)..end].Trim();
        }
    }

    private sealed class RepairingTranslationClient : IOllamaClient
    {
        public int CallCount { get; private set; }

        public Task TestConnectionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<OllamaModelSummary>> ListModelsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<OllamaModelSummary>>(
                [new OllamaModelSummary("llama-test", "llama-test", null, 0, null, null, null, null)]);
        }

        public Task PullModelAsync(string modelName, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task PullModelAsync(
            string modelName,
            Func<OllamaModelPullProgress, CancellationToken, Task> onProgress,
            CancellationToken cancellationToken = default)
        {
            return onProgress(new OllamaModelPullProgress("success", null, null, 100), cancellationToken);
        }

        public Task DeleteModelAsync(string modelName, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ChatSmokeAsync(string modelName, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task EmbeddingsSmokeAsync(string modelName, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> GenerateChatAsync(
            string modelName,
            IReadOnlyList<OllamaChatMessage> messages,
            int? numCtx = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            string prompt = messages[^1].Content;
            string source = ExtractTaggedSource(prompt);
            if (CallCount > 1)
            {
                return Task.FromResult($"Translated: {source}");
            }

            return source.Contains("{name}", StringComparison.Ordinal)
                ? Task.FromResult("Translated: Ciao 123")
                : Task.FromResult($"Translated: {source}");
        }

        public Task<OllamaModelDetails> ShowModelAsync(string modelName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new OllamaModelDetails(modelName, null));
        }

        public Task<IReadOnlyList<IReadOnlyList<float>>> GenerateEmbeddingsAsync(
            string modelName,
            IReadOnlyList<string> inputs,
            int? numCtx = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        private static string ExtractTaggedSource(string prompt)
        {
            const string startTag = "<source_text>";
            const string endTag = "</source_text>";
            int start = prompt.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
            int end = prompt.IndexOf(endTag, StringComparison.OrdinalIgnoreCase);
            if (start < 0 || end <= start)
            {
                return prompt.Trim();
            }

            start += startTag.Length;
            return prompt[start..end].Trim();
        }
    }

    private sealed class TimeoutThenSuccessTranslationClient : EchoTranslationClient
    {
        public override Task<string> GenerateChatAsync(
            string modelName,
            IReadOnlyList<OllamaChatMessage> messages,
            int? numCtx = null,
            CancellationToken cancellationToken = default)
        {
            IncrementCallCount();
            if (CallCount == 1)
            {
                throw new OllamaApiException(OllamaErrorKind.Timeout, "timeout");
            }

            string source = ExtractSource(messages[^1].Content);
            return Task.FromResult($"Translated: {source}");
        }
    }

    private sealed class AlwaysInvalidPlaceholderTranslationClient : EchoTranslationClient
    {
        public override Task<string> GenerateChatAsync(
            string modelName,
            IReadOnlyList<OllamaChatMessage> messages,
            int? numCtx = null,
            CancellationToken cancellationToken = default)
        {
            IncrementCallCount();
            return Task.FromResult("Translated without placeholder");
        }
    }

    private sealed class StubOllamaSettingsService : IOllamaSettingsService
    {
        public Task ClearMissingDefaultModelAsync(string modelName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<OllamaSettings> GetAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new OllamaSettings(
                OllamaEndpointOptions.DefaultBaseUrl,
                null,
                null,
                null,
                60,
                1));
        }

        public Task<OllamaSettings> UpdateAsync(OllamaSettings settings, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubPerformanceSettingsService : IPerformanceSettingsService
    {
        private readonly PerformanceSettings settings;
        private int getCallCount;

        public StubPerformanceSettingsService(PerformanceSettings settings)
        {
            this.settings = settings;
        }

        public Task<PerformanceSettings> GetAsync(CancellationToken cancellationToken = default)
        {
            getCallCount++;
            return Task.FromResult(settings);
        }

        public int GetCallCount => getCallCount;

        public Task<PerformanceSettings> UpdateAsync(PerformanceSettings settings, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class TempStorage : IDisposable
    {
        private TempStorage(string root)
        {
            Root = root;
            Paths = AppStoragePaths.FromRoot(root);
            Descriptor = new LocalSqliteStoreDescriptor(Paths);
        }

        public string Root { get; }

        public AppStoragePaths Paths { get; }

        public LocalSqliteStoreDescriptor Descriptor { get; }

        public static TempStorage Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "OnlyRag.TranslationJob.Tests", Guid.NewGuid().ToString("N"));
            return new TempStorage(root);
        }

        public async Task InitializeAsync()
        {
            LocalSqliteConnectionFactory connectionFactory = CreateConnectionFactory();
            LocalSqliteMigrator migrator = new(Descriptor, connectionFactory);
            LocalSqliteStorageService storage = new(Descriptor, migrator);
            await storage.InitializeAsync();
        }

        public LocalSqliteConnectionFactory CreateConnectionFactory()
        {
            return new LocalSqliteConnectionFactory(Descriptor);
        }

        public SqliteLocalJobQueue CreateQueue()
        {
            return new SqliteLocalJobQueue(CreateConnectionFactory(), LocalJobQueueDescriptor.Default);
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
