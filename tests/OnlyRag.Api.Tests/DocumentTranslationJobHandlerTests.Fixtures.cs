using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Api.Tests;

public sealed partial class DocumentTranslationJobHandlerTests
{
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
