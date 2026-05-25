using OnlyRag.Api;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Retrieval;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Infrastructure.Vector;

namespace OnlyRag.Api.Tests;

public sealed class ChatServiceQdrantIntegrationTests
{
    [Fact]
    public async Task SendAsync_WithDocuments_UsesQdrantRetrievalAndReturnsSourceSnippet()
    {
        using TempChatStorage tempStorage = TempChatStorage.Create();
        await tempStorage.InitializeAsync();
        ChatRetrievalServices retrievalServices = tempStorage.CreateRetrievalServices(
            new StaticQueryEmbeddingGenerator("embed-rag", [1f, 0f]));
        ImportedDocument document = await tempStorage.CreateDocumentAsync(
            "doc-rag-vector",
            "protocollo.txt",
            [
                "Protocollo ZETA-777 confermato nella procedura interna.",
                "Appendice amministrativa senza il codice rilevante."
            ]);
        await EmbedDocumentAsync(
            retrievalServices,
            document.Id,
            "embed-rag",
            [[1f, 0f], [0f, 1f]]);

        FakeOllamaClient ollama = new("gemma3:4b", "Risposta basata sul protocollo.");
        InMemoryChatHistoryRepository history = new();
        ChatService service = new(ollama, retrievalServices.Retrieval, history, new StubOllamaSettingsService());

        ChatResponse response = await service.SendAsync(new ChatRequest(
            "Domanda semantica senza keyword del documento",
            "gemma3:4b",
            UseDocuments: true,
            SelectedDocumentIds: [document.Id],
            ConversationId: "conv-qdrant"));

        Assert.True(response.UsedDocuments);
        Assert.Contains(response.Sources, source =>
            source.DocumentId == document.Id &&
            source.DocumentName == "protocollo.txt" &&
            source.Snippet.Contains("ZETA-777", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(ollama.LastMessages, message =>
            message.Role == "system" &&
            message.Content.Contains("ZETA-777", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, history.Messages.Count);
    }

    private static async Task EmbedDocumentAsync(
        ChatRetrievalServices services,
        long documentId,
        string model,
        IReadOnlyList<IReadOnlyList<float>> vectors)
    {
        IReadOnlyList<DocumentChunkForEmbedding> chunks =
            await services.Embeddings.ListChunksNeedingEmbeddingAsync(documentId, model, 0, vectors.Count);

        Assert.Equal(vectors.Count, chunks.Count);
        for (int index = 0; index < chunks.Count; index++)
        {
            services.VectorStore.AddVector(chunks[index], model, vectors[index]);
            await services.Embeddings.MarkChunkIndexedAsync(
                chunks[index].Id,
                model,
                chunks[index].ContentHash,
                vectors[index].Count,
                services.VectorStore.BuildCollectionName(model, vectors[index].Count),
                services.VectorStore.BuildPointId(chunks[index].Id));
        }
    }

    private sealed record ChatRetrievalServices(
        SqliteEmbeddingRepository Embeddings,
        FakeQdrantVectorStore VectorStore,
        IHybridRetrievalService Retrieval);

    private sealed class StaticQueryEmbeddingGenerator : IQueryEmbeddingGenerator
    {
        private readonly string model;
        private readonly IReadOnlyList<float> vector;

        public StaticQueryEmbeddingGenerator(string model, IReadOnlyList<float> vector)
        {
            this.model = model;
            this.vector = vector;
        }

        public Task<QueryEmbeddingResult> GenerateAsync(string query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new QueryEmbeddingResult(model, vector));
        }
    }

    private sealed class TempChatStorage : IDisposable
    {
        private TempChatStorage(string root)
        {
            Root = root;
            Paths = AppStoragePaths.FromRoot(root);
            Descriptor = new LocalSqliteStoreDescriptor(Paths);
        }

        public string Root { get; }

        public AppStoragePaths Paths { get; }

        public LocalSqliteStoreDescriptor Descriptor { get; }

        public static TempChatStorage Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "OnlyRag.Chat.Tests", Guid.NewGuid().ToString("N"));
            return new TempChatStorage(root);
        }

        public async Task InitializeAsync()
        {
            LocalSqliteConnectionFactory connectionFactory = CreateConnectionFactory();
            LocalSqliteMigrator migrator = new(Descriptor, connectionFactory);
            LocalSqliteStorageService storage = new(Descriptor, migrator);
            await storage.InitializeAsync();
        }

        public ChatRetrievalServices CreateRetrievalServices(IQueryEmbeddingGenerator queryEmbeddingGenerator)
        {
            LocalSqliteConnectionFactory connectionFactory = CreateConnectionFactory();
            SqliteEmbeddingRepository embeddings = new(connectionFactory);
            FakeQdrantVectorStore vectorStore = new();
            HybridRetrievalService retrieval = new(
                new SqliteDocumentRepository(connectionFactory),
                embeddings,
                new SqliteKeywordSearchService(connectionFactory),
                vectorStore,
                new SqliteRetrievalChunkRepository(connectionFactory),
                queryEmbeddingGenerator,
                new HybridRetrievalOptions(
                    DefaultTopK: 5,
                    KeywordTopK: 5,
                    VectorTopK: 5,
                    MaxTopK: 8,
                    SnippetMaxCharacters: 180,
                    MaxContextCharacters: 1000));

            return new ChatRetrievalServices(embeddings, vectorStore, retrieval);
        }

        public async Task<ImportedDocument> CreateDocumentAsync(
            string uid,
            string name,
            IReadOnlyList<string> pageTexts)
        {
            SqliteDocumentRepository documents = new(CreateConnectionFactory());
            DateTimeOffset now = DateTimeOffset.UtcNow;
            ImportedDocument document = await documents.CreateAsync(new CreateDocumentRecordRequest(
                uid,
                name,
                Path.Combine(Root, name),
                uid,
                "text/plain",
                ".txt",
                100,
                DocumentStatus.Indexed,
                PageCount: 0,
                CurrentJobId: null,
                LastError: null,
                now,
                now));

            int ordinal = 0;
            for (int index = 0; index < pageTexts.Count; index++)
            {
                string text = pageTexts[index];
                await documents.SaveIngestedPageAsync(
                    document.Id,
                    new IngestedDocumentPage(index + 1, text),
                    [new IngestedDocumentChunk(index + 1, index + 1, ordinal, text, text.Length / 4, $"hash-{uid}-{ordinal}")],
                    pageTexts.Count);
                ordinal++;
            }

            return (await documents.GetAsync(document.Id))!;
        }

        private LocalSqliteConnectionFactory CreateConnectionFactory()
        {
            return new LocalSqliteConnectionFactory(Descriptor);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class FakeQdrantVectorStore : IQdrantVectorStore
    {
        private readonly List<(DocumentChunkForEmbedding Chunk, string Model, IReadOnlyList<float> Vector)> vectors = [];

        public string BackendName => "Qdrant fake";

        public int MaxSearchableVectors => int.MaxValue;

        public bool IsVectorStoragePersistent => true;

        public void AddVector(DocumentChunkForEmbedding chunk, string model, IReadOnlyList<float> vector)
        {
            vectors.Add((chunk, model, vector));
        }

        public string BuildCollectionName(string model, int dimensions) => $"onlyrag_{dimensions}_test";

        public string BuildPointId(long chunkId) => chunkId.ToString();

        public Task VerifyAvailabilityAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpsertChunkAsync(
            long chunkId,
            long documentId,
            int chunkIndex,
            string model,
            string contentHash,
            IReadOnlyList<float> vector,
            CancellationToken cancellationToken = default)
        {
            vectors.Add((new DocumentChunkForEmbedding(chunkId, documentId, chunkIndex, string.Empty, contentHash), model, vector));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            string model,
            IReadOnlyList<float> queryVector,
            IReadOnlyCollection<long> documentIds,
            int limit,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<VectorSearchResult> results = vectors
                .Where(item => item.Model == model && documentIds.Contains(item.Chunk.DocumentId))
                .Select(item => new VectorSearchResult(
                    item.Chunk.Id,
                    item.Chunk.DocumentId,
                    item.Chunk.ChunkIndex,
                    Cosine(queryVector, item.Vector)))
                .OrderByDescending(result => result.Score)
                .Take(limit)
                .ToArray();
            return Task.FromResult(results);
        }

        public Task DeleteDocumentAsync(
            string model,
            int dimensions,
            long documentId,
            CancellationToken cancellationToken = default)
        {
            vectors.RemoveAll(item => item.Model == model && item.Chunk.DocumentId == documentId);
            return Task.CompletedTask;
        }

        private static double Cosine(IReadOnlyList<float> left, IReadOnlyList<float> right)
        {
            double dot = 0;
            double leftNorm = 0;
            double rightNorm = 0;
            for (int index = 0; index < Math.Min(left.Count, right.Count); index++)
            {
                dot += left[index] * right[index];
                leftNorm += left[index] * left[index];
                rightNorm += right[index] * right[index];
            }

            return leftNorm == 0 || rightNorm == 0 ? 0 : dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
        }
    }

    private sealed class FakeOllamaClient : IOllamaClient
    {
        private readonly string installedModel;
        private readonly string answer;

        public FakeOllamaClient(string installedModel, string answer)
        {
            this.installedModel = installedModel;
            this.answer = answer;
        }

        public IReadOnlyList<OllamaChatMessage> LastMessages { get; private set; } = [];

        public Task ChatSmokeAsync(string modelName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteModelAsync(string modelName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task EmbeddingsSmokeAsync(string modelName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<string> GenerateChatAsync(
            string modelName,
            IReadOnlyList<OllamaChatMessage> messages,
            int? numCtx = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = messages;
            return Task.FromResult(answer);
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
            return Task.FromResult<IReadOnlyList<IReadOnlyList<float>>>([]);
        }

        public Task<IReadOnlyList<OllamaModelSummary>> ListModelsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<OllamaModelSummary>>(
                [new OllamaModelSummary(installedModel, installedModel, null, 1, null, null, null, null)]);
        }

        public Task PullModelAsync(string modelName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
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

    private sealed class InMemoryChatHistoryRepository : IChatHistoryRepository
    {
        public List<ChatHistoryRecord> Messages { get; } = [];

        public Task AppendMessageAsync(
            string conversationId,
            string role,
            string content,
            string? model,
            string? metadataJson,
            CancellationToken cancellationToken = default)
        {
            Messages.Add(new ChatHistoryRecord(
                Messages.Count + 1,
                conversationId,
                role,
                content,
                model,
                metadataJson,
                DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ChatHistoryRecord>> ListRecentMessagesAsync(
            string conversationId,
            int maxMessages,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ChatHistoryRecord> result = Messages
                .Where(message => message.ConversationId == conversationId)
                .TakeLast(maxMessages)
                .ToArray();
            return Task.FromResult(result);
        }
    }
}
