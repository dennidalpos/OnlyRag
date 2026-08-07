using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Vector;
using OnlyRag.Worker;

namespace OnlyRag.Api.Tests;

public sealed partial class InProcessBackendTests
{
    [Fact]
    public async Task PopulatedWorkflow_CoversJobsRetrievalChatTranslationSettingsOcrStatusAndShutdown()
    {
        await using FakeOllamaServer ollama = await FakeOllamaServer.StartAsync();
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { QdrantVectorStore = new FakeQdrantVectorStore() });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        OllamaSettings ollamaSettings = new(
            ollama.BaseUrl,
            "chat-model",
            "embed-model",
            "translation-model",
            30,
            1);
        using HttpResponseMessage settingsResponse = await httpClient.PutAsJsonAsync(
            "/api/settings/ollama",
            ollamaSettings,
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, settingsResponse.StatusCode);

        PerformanceSettings performanceSettings = new(1, 1, 1, 1, 8, 30, false);
        using HttpResponseMessage performanceResponse = await httpClient.PutAsJsonAsync(
            "/api/settings/performance",
            performanceSettings,
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, performanceResponse.StatusCode);

        ImportedDocument imported = await ImportTextDocumentAsync(httpClient);
        ImportedDocument indexed = await WaitForAsync(
            async () => (await httpClient.GetFromJsonAsync<ImportedDocument>(
                $"/api/documents/{imported.Id}",
                JsonOptions))!,
            document => document.Status == DocumentStatus.Indexed && document.ChunkCount > 0,
            "document indexing");
        DocumentEmbeddingStatusResponse embeddingStatus = await WaitForAsync(
            async () => (await httpClient.GetFromJsonAsync<DocumentEmbeddingStatusResponse>(
                $"/api/documents/{imported.Id}/embedding-status",
                JsonOptions))!,
            status => status.ChunkCount > 0 && status.EmbeddedChunkCount == status.ChunkCount,
            "document embeddings");

        DocumentOcrStatusResponse? ocrStatus = await httpClient.GetFromJsonAsync<DocumentOcrStatusResponse>(
            $"/api/documents/{imported.Id}/ocr-status",
            JsonOptions);
        Assert.NotNull(ocrStatus);
        Assert.Equal(indexed.Id, ocrStatus.DocumentId);

        using HttpResponseMessage searchResponse = await httpClient.PostAsJsonAsync(
            "/api/search",
            new DocumentSearchRequest("Quale codice operativo e citato?", [indexed.Id], 5),
            JsonOptions);
        DocumentSearchResponse? search = await searchResponse.Content.ReadFromJsonAsync<DocumentSearchResponse>(JsonOptions);
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        Assert.NotNull(search);
        Assert.Contains(search.Results, result => result.Snippet.Contains("ZETA-777", StringComparison.OrdinalIgnoreCase));

        using HttpResponseMessage chatResponse = await httpClient.PostAsJsonAsync(
            "/api/chat",
            new ChatRequest("Quale codice operativo e citato?", "chat-model", true, [indexed.Id], "populated-flow"),
            JsonOptions);
        ChatResponse? chat = await chatResponse.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions);
        Assert.Equal(HttpStatusCode.OK, chatResponse.StatusCode);
        Assert.NotNull(chat);
        Assert.True(chat.UsedDocuments);
        Assert.Contains("ZETA-777", chat.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(chat.Sources, source => source.Snippet.Contains("ZETA-777", StringComparison.OrdinalIgnoreCase));

        using HttpResponseMessage translationResponse = await httpClient.PostAsJsonAsync(
            "/api/translations",
            new CreateTranslationRequest(indexed.Id, "English", "translation-model"),
            JsonOptions);
        TranslationDetailResponse? queuedTranslation =
            await translationResponse.Content.ReadFromJsonAsync<TranslationDetailResponse>(JsonOptions);
        Assert.Equal(HttpStatusCode.OK, translationResponse.StatusCode);
        Assert.NotNull(queuedTranslation);

        TranslationDetailResponse completedTranslation = await WaitForAsync(
            async () => (await httpClient.GetFromJsonAsync<TranslationDetailResponse>(
                $"/api/translations/{queuedTranslation.Translation.Id}",
                JsonOptions))!,
            detail => detail.Translation.Status == "Completed"
                && detail.Units.Count > 0
                && detail.Units.All(unit => unit.Status == "Completed"),
            "document translation");
        Assert.Equal(completedTranslation.Translation.UnitCount, completedTranslation.Translation.CompletedUnitCount);

        TranslationCompareResponse? compare = await httpClient.GetFromJsonAsync<TranslationCompareResponse>(
            $"/api/translations/{completedTranslation.Translation.Id}/compare?page=1",
            JsonOptions);
        Assert.NotNull(compare);
        Assert.NotEmpty(compare.Units);

        using HttpResponseMessage exportResponse = await httpClient.PostAsJsonAsync(
            $"/api/translations/{completedTranslation.Translation.Id}/export",
            new TranslationExportRequest("txt"),
            JsonOptions);
        TranslationExportResponse? exported = await exportResponse.Content.ReadFromJsonAsync<TranslationExportResponse>(JsonOptions);
        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
        Assert.NotNull(exported);
        Assert.True(File.Exists(exported.OutputPath));
        AssertPathUnderRoot(tempDescriptor.Root, exported.OutputPath);

        JobStatus[] activeStatuses = [JobStatus.Pending, JobStatus.Running, JobStatus.Pausing, JobStatus.Paused];
        LocalJob[] jobs = await WaitForAsync(
            async () => (await httpClient.GetFromJsonAsync<LocalJob[]>("/api/jobs", JsonOptions))!,
            currentJobs => currentJobs != null && currentJobs.All(job => !activeStatuses.Contains(job.Status)),
            "all active jobs completion");
        Assert.NotNull(jobs);
        Assert.All(jobs, job => Assert.DoesNotContain(job.Status, activeStatuses));

        using HttpResponseMessage shutdownResponse = await httpClient.PostAsync("/api/app/prepare-shutdown", content: null);
        AppShutdownPreparationResponse? shutdown =
            await shutdownResponse.Content.ReadFromJsonAsync<AppShutdownPreparationResponse>(JsonOptions);
        Assert.Equal(HttpStatusCode.OK, shutdownResponse.StatusCode);
        Assert.NotNull(shutdown);
        Assert.True(shutdown.IsComplete);
        Assert.Equal(0, shutdown.ActiveJobCount);
        Assert.Equal(0, shutdown.CancelledJobCount);

        Assert.Equal(indexed.ChunkCount, embeddingStatus.ChunkCount);
        Assert.True(File.Exists(tempDescriptor.Descriptor.StoragePaths.DatabasePath));
        Assert.NotEmpty(ListOriginalFiles(tempDescriptor));
        foreach (string originalPath in ListOriginalFiles(tempDescriptor))
        {
            AssertPathUnderRoot(tempDescriptor.Root, originalPath);
        }
    }

    private static async Task<ImportedDocument> ImportTextDocumentAsync(HttpClient httpClient)
    {
        const string documentText = "Protocollo ZETA-777 attivo per reparto ricerca.\n"
            + "La procedura resta locale e verificabile nel workflow OnlyRag.";
        using MultipartFormDataContent content = new();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(documentText)), "files", "populated-workflow.txt");

        using HttpResponseMessage importResponse = await httpClient.PostAsync("/api/documents/import", content);
        DocumentImportResponse? importPayload =
            await importResponse.Content.ReadFromJsonAsync<DocumentImportResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        Assert.NotNull(importPayload);
        DocumentImportResult result = Assert.Single(importPayload.Documents);
        Assert.False(result.Deduplicated);
        return result.Document;
    }

    private static async Task<T> WaitForAsync<T>(
        Func<Task<T>> readAsync,
        Func<T, bool> isReady,
        string description)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        T last = await readAsync();
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (isReady(last))
            {
                return last;
            }

            await Task.Delay(100);
            last = await readAsync();
        }

        string lastJson = JsonSerializer.Serialize(last, JsonOptions);
        throw new TimeoutException($"Timed out waiting for {description}. Last observed value: {lastJson}");
    }

    private static void AssertPathUnderRoot(string root, string path)
    {
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string normalizedPath = Path.GetFullPath(path);
        Assert.StartsWith(normalizedRoot, normalizedPath, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeQdrantVectorStore : IQdrantVectorStore
    {
        private readonly List<StoredVector> vectors = [];

        public string BackendName => "Qdrant fake";

        public int MaxSearchableVectors => int.MaxValue;

        public bool IsVectorStoragePersistent => true;

        public string BuildCollectionName(string model, int dimensions) => $"onlyrag_{dimensions}_test";

        public string BuildPointId(long chunkId) => chunkId.ToString(System.Globalization.CultureInfo.InvariantCulture);

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
            lock (vectors)
            {
                vectors.RemoveAll(item => item.ChunkId == chunkId && item.Model == model);
                vectors.Add(new StoredVector(chunkId, documentId, chunkIndex, model, vector.ToArray()));
            }

            return Task.CompletedTask;
        }

        public Task UpsertChunkBatchAsync(
            IReadOnlyList<OnlyRag.Infrastructure.Vector.QdrantChunkPayload> chunks,
            CancellationToken cancellationToken = default)
        {
            lock (vectors)
            {
                foreach (var c in chunks)
                {
                    vectors.RemoveAll(item => item.ChunkId == c.ChunkId && item.Model == c.Model);
                    vectors.Add(new StoredVector(c.ChunkId, c.DocumentId, c.ChunkIndex, c.Model, c.Vector.ToArray()));
                }
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            string model,
            IReadOnlyList<float> queryVector,
            IReadOnlyCollection<long> documentIds,
            int limit,
            CancellationToken cancellationToken = default)
        {
            StoredVector[] snapshot;
            lock (vectors)
            {
                snapshot = vectors.ToArray();
            }

            IReadOnlyList<VectorSearchResult> results = snapshot
                .Where(item => item.Model == model && documentIds.Contains(item.DocumentId))
                .Select(item => new VectorSearchResult(
                    item.ChunkId,
                    item.DocumentId,
                    item.ChunkIndex,
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
            lock (vectors)
            {
                vectors.RemoveAll(item => item.Model == model && item.DocumentId == documentId);
            }

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

        public Task OptimizeCollectionAsync(string model, int dimensions, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        private sealed record StoredVector(
            long ChunkId,
            long DocumentId,
            int ChunkIndex,
            string Model,
            IReadOnlyList<float> Vector);
    }

}
