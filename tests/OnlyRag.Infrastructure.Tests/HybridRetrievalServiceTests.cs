using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Retrieval;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Infrastructure.Vector;

namespace OnlyRag.Infrastructure.Tests;

public sealed class HybridRetrievalServiceTests
{
    [Fact]
    public async Task SearchAsync_ReturnsResultForSingleSelectedDocument()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();
        TestServices services = tempStorage.CreateServices(new StaticQueryEmbeddingGenerator([1f, 0f]));
        ImportedDocument document = await tempStorage.CreateDocumentAsync(
            "doc-1",
            "manuale.txt",
            [
                "Codice pratica ABC-123 del 2026.",
                "Contenuto generico senza il riferimento."
            ]);
        await EmbedDocumentAsync(services, document.Id, [[1f, 0f], [0f, 1f]]);

        DocumentSearchResponse response = await services.Retrieval.SearchAsync(
            new DocumentSearchRequest("ABC-123", [document.Id], 5));

        DocumentSearchResult result = Assert.Single(
            response.Results,
            result => result.Snippet.Contains("ABC-123", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(document.Id, result.DocumentId);
        Assert.Equal("manuale.txt", result.DocumentName);
        Assert.Contains("ABC-123", result.Snippet, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, result.PageStart);
        Assert.Equal(result.ChunkId, response.Results[0].ChunkId);
    }

    [Fact]
    public async Task SearchAsync_ReturnsResultsForMultipleSelectedDocuments()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();
        TestServices services = tempStorage.CreateServices(new StaticQueryEmbeddingGenerator([1f, 0f]));
        ImportedDocument first = await tempStorage.CreateDocumentAsync("doc-1", "alpha.txt", ["Scadenza 2026-04-25."]);
        ImportedDocument second = await tempStorage.CreateDocumentAsync("doc-2", "beta.txt", ["Scadenza 2026-05-01."]);
        await EmbedDocumentAsync(services, first.Id, [[1f, 0f]]);
        await EmbedDocumentAsync(services, second.Id, [[0.9f, 0.1f]]);

        DocumentSearchResponse response = await services.Retrieval.SearchAsync(
            new DocumentSearchRequest("Scadenza", [first.Id, second.Id], 5));

        Assert.Equal(2, response.Results.Select(result => result.DocumentId).Distinct().Count());
        Assert.Contains(response.Results, result => result.DocumentId == first.Id);
        Assert.Contains(response.Results, result => result.DocumentId == second.Id);
    }

    [Fact]
    public async Task SearchAsync_FiltersBySelectedDocumentIds()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();
        TestServices services = tempStorage.CreateServices(new StaticQueryEmbeddingGenerator([1f, 0f]));
        ImportedDocument selected = await tempStorage.CreateDocumentAsync("doc-1", "selected.txt", ["Riferimento Q4-2026."]);
        ImportedDocument excluded = await tempStorage.CreateDocumentAsync("doc-2", "excluded.txt", ["Riferimento Q4-2026."]);
        await EmbedDocumentAsync(services, selected.Id, [[1f, 0f]]);
        await EmbedDocumentAsync(services, excluded.Id, [[1f, 0f]]);

        DocumentSearchResponse response = await services.Retrieval.SearchAsync(
            new DocumentSearchRequest("Q4-2026", [selected.Id], 5));

        Assert.All(response.Results, result => Assert.Equal(selected.Id, result.DocumentId));
    }

    [Fact]
    public async Task SearchAsync_DeduplicatesKeywordAndVectorHitsByChunkId()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();
        TestServices services = tempStorage.CreateServices(new StaticQueryEmbeddingGenerator([1f, 0f]));
        ImportedDocument document = await tempStorage.CreateDocumentAsync("doc-1", "dedupe.txt", ["Protocollo ZX-77 prioritario."]);
        await EmbedDocumentAsync(services, document.Id, [[1f, 0f]]);

        DocumentSearchResponse response = await services.Retrieval.SearchAsync(
            new DocumentSearchRequest("ZX-77", [document.Id], 10));

        Assert.Single(response.Results);
        Assert.Equal(response.Results.Count, response.Results.Select(result => result.ChunkId).Distinct().Count());
    }

    [Fact]
    public async Task SearchAsync_FailsWhenVectorSearchIsUnavailable()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();
        TestServices services = tempStorage.CreateServices(new UnavailableQueryEmbeddingGenerator());
        ImportedDocument document = await tempStorage.CreateDocumentAsync("doc-1", "keyword-only.txt", ["Numero fattura 98765."]);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.Retrieval.SearchAsync(new DocumentSearchRequest("98765", [document.Id], 5)));

        Assert.Contains("retrieval Qdrant", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchAsync_UsesBm25RankForKeywordOrdering()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();
        TestServices services = tempStorage.CreateServices(new StaticQueryEmbeddingGenerator([1f, 0f]));
        ImportedDocument document = await tempStorage.CreateDocumentAsync(
            "doc-bm25",
            "bm25.txt",
            [
                "alpha alpha alpha alpha riferimento forte.",
                "alpha riferimento debole."
            ]);

        DocumentSearchResponse response = await services.Retrieval.SearchAsync(
            new DocumentSearchRequest("alpha", [document.Id], 5));

        Assert.True(response.Results.Count >= 2);
        Assert.Contains("forte", response.Results[0].Snippet, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.Results[0].Score >= response.Results[1].Score);
    }

    [Fact]
    public async Task SearchAsync_UsesQdrantWithoutKeywordOnlyFallback()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();
        TestServices services = tempStorage.CreateServices(new StaticQueryEmbeddingGenerator([1f, 0f]));
        ImportedDocument document = await tempStorage.CreateDocumentAsync(
            "doc-qdrant",
            "qdrant.txt",
            [
                "Ricerca vettoriale primo chunk.",
                "Ricerca vettoriale secondo chunk."
            ]);
        await EmbedDocumentAsync(services, document.Id, [[1f, 0f], [0.9f, 0.1f]]);

        DocumentSearchResponse response = await services.Retrieval.SearchAsync(
            new DocumentSearchRequest("Ricerca", [document.Id], 5));

        Assert.NotEmpty(response.Results);
        Assert.Contains("Qdrant", response.VectorBackend, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fallback", response.VectorBackend, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchAsync_UsesFts4FallbackWhenFts5TableIsUnavailable()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();
        await tempStorage.ReplaceFtsIndexWithFts4Async();
        TestServices services = tempStorage.CreateServices(new StaticQueryEmbeddingGenerator([1f, 0f]));
        ImportedDocument document = await tempStorage.CreateDocumentAsync("doc-1", "fallback.txt", ["Riferimento contratto CN-445."]);
        await EmbedDocumentAsync(services, document.Id, [[1f, 0f]]);

        DocumentSearchResponse response = await services.Retrieval.SearchAsync(
            new DocumentSearchRequest("CN-445", [document.Id], 5));

        DocumentSearchResult result = Assert.Single(response.Results);
        Assert.Equal(document.Id, result.DocumentId);
        Assert.Contains("CN-445", result.Snippet, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("SQLite FTS indexed fallback", response.KeywordBackend);
    }

    private static async Task EmbedDocumentAsync(
        TestServices services,
        long documentId,
        IReadOnlyList<IReadOnlyList<float>> vectors)
    {
        IReadOnlyList<DocumentChunkForEmbedding> chunks =
            await services.Embeddings.ListChunksNeedingEmbeddingAsync(documentId, "embed-test", 0, vectors.Count);

        Assert.Equal(vectors.Count, chunks.Count);
        for (int index = 0; index < chunks.Count; index++)
        {
            services.VectorStore.AddVector(chunks[index], "embed-test", vectors[index]);
            await services.Embeddings.MarkChunkIndexedAsync(
                chunks[index].Id,
                "embed-test",
                chunks[index].ContentHash,
                vectors[index].Count,
                services.VectorStore.BuildCollectionName("embed-test", vectors[index].Count),
                services.VectorStore.BuildPointId(chunks[index].Id));
        }
    }

    private sealed record TestServices(
        SqliteEmbeddingRepository Embeddings,
        FakeQdrantVectorStore VectorStore,
        IHybridRetrievalService Retrieval);

    private sealed class StaticQueryEmbeddingGenerator : IQueryEmbeddingGenerator
    {
        private readonly IReadOnlyList<float> vector;

        public StaticQueryEmbeddingGenerator(IReadOnlyList<float> vector)
        {
            this.vector = vector;
        }

        public Task<QueryEmbeddingResult> GenerateAsync(string query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new QueryEmbeddingResult("embed-test", vector));
        }
    }

    private sealed class UnavailableQueryEmbeddingGenerator : IQueryEmbeddingGenerator
    {
        public Task<QueryEmbeddingResult> GenerateAsync(string query, CancellationToken cancellationToken = default)
        {
            throw new QueryEmbeddingUnavailableException("vector unavailable");
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
            string root = Path.Combine(Path.GetTempPath(), "OnlyRag.Retrieval.Tests", Guid.NewGuid().ToString("N"));
            return new TempStorage(root);
        }

        public async Task InitializeAsync()
        {
            LocalSqliteConnectionFactory connectionFactory = CreateConnectionFactory();
            LocalSqliteSchemaInitializer initializer = new(Descriptor, connectionFactory);
            LocalSqliteStorageService storage = new(Descriptor, initializer);
            await storage.InitializeAsync();
        }

        public TestServices CreateServices(
            IQueryEmbeddingGenerator queryEmbeddingGenerator)
        {
            LocalSqliteConnectionFactory connectionFactory = CreateConnectionFactory();
            SqliteDocumentRepository documents = new(connectionFactory);
            SqliteEmbeddingRepository embeddings = new(connectionFactory);
            SqliteKeywordSearchService keywordSearch = new(connectionFactory);
            FakeQdrantVectorStore vectorSearch = new();
            SqliteRetrievalChunkRepository chunks = new(connectionFactory);
            HybridRetrievalService retrieval = new(
                documents,
                embeddings,
                keywordSearch,
                vectorSearch,
                chunks,
                queryEmbeddingGenerator,
                new HybridRetrievalOptions(
                    DefaultTopK: 5,
                    KeywordTopK: 10,
                    VectorTopK: 10,
                    MaxTopK: 10,
                    SnippetMaxCharacters: 180,
                    MaxContextCharacters: 1000));

            return new TestServices(embeddings, vectorSearch, retrieval);
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

        public async Task ReplaceFtsIndexWithFts4Async()
        {
            await using Microsoft.Data.Sqlite.SqliteConnection connection = await CreateConnectionFactory().OpenConnectionAsync();
            await using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                DROP TRIGGER IF EXISTS chunks_ai;
                DROP TRIGGER IF EXISTS chunks_ad;
                DROP TRIGGER IF EXISTS chunks_au;
                DROP TABLE IF EXISTS chunks_fts;

                CREATE VIRTUAL TABLE chunks_fts USING fts4(
                    chunk_id,
                    content,
                    notindexed=chunk_id
                );

                CREATE TRIGGER chunks_ai AFTER INSERT ON chunks BEGIN
                    INSERT INTO chunks_fts(rowid, chunk_id, content)
                    VALUES (new.id, new.id, new.content);
                END;

                CREATE TRIGGER chunks_ad AFTER DELETE ON chunks BEGIN
                    DELETE FROM chunks_fts WHERE rowid = old.id;
                END;

                CREATE TRIGGER chunks_au AFTER UPDATE OF content ON chunks BEGIN
                    DELETE FROM chunks_fts WHERE rowid = old.id;
                    INSERT INTO chunks_fts(rowid, chunk_id, content)
                    VALUES (new.id, new.id, new.content);
                END;
                """;
            await command.ExecuteNonQueryAsync();
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

        public Task UpsertChunkAsync(long chunkId, long documentId, int chunkIndex, string model, string contentHash, IReadOnlyList<float> vector, CancellationToken cancellationToken = default)
        {
            vectors.Add((new DocumentChunkForEmbedding(chunkId, documentId, chunkIndex, string.Empty, contentHash), model, vector));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(string model, IReadOnlyList<float> queryVector, IReadOnlyCollection<long> documentIds, int limit, CancellationToken cancellationToken = default)
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

        public Task DeleteDocumentAsync(string model, int dimensions, long documentId, CancellationToken cancellationToken = default)
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
}
