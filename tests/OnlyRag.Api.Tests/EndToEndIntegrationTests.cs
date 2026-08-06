using System.Text.Json;
using OnlyRag.Api;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Agent;
using OnlyRag.Infrastructure.Retrieval;
using OnlyRag.Infrastructure.Storage;
using Xunit;

namespace OnlyRag.Api.Tests;

public sealed class EndToEndIntegrationTests
{
    [Fact]
    public async Task ContextAssembly_SeparatesSnippetAndParentContent()
    {
        FakeOllamaClient ollama = new("gemma3:4b", "Risposta verificata. **(Source: SpecDoc.pdf, p. 5)**");

        DocumentSearchResult result = new(
            DocumentId: 42,
            DocumentName: "SpecDoc.pdf",
            PageStart: 5,
            PageEnd: 5,
            ChunkId: 101,
            Snippet: "Ranking snippet match for query",
            Score: 0.95,
            ReRankScore: 0.98,
            ParentContent: "Full parent context section explaining all implementation details of SpecDoc.",
            ChunkLevel: "Child",
            SectionHeading: "Section 5.1 Architecture");

        DocumentSearchResponse searchResponse = new(
            Results: [result],
            Documents: [new DocumentSearchDocumentStatus(42, "SpecDoc.pdf", DocumentStatus.Indexed, true, "Ready", 5, 5)],
            KeywordBackend: "FTS5",
            VectorBackend: "Qdrant",
            MaxContextCharacters: 4000);

        StaticRetrievalService retrieval = new(searchResponse);
        InMemoryChatHistoryRepository history = new();

        ChatService chatService = new(ollama, retrieval, history, new StubOllamaSettingsService());

        ChatResponse response = await chatService.SendAsync(new ChatRequest(
            "Spiega l'architettura",
            "gemma3:4b",
            UseDocuments: true,
            SelectedDocumentIds: [42],
            ConversationId: "conv_e2e_1"));

        Assert.True(response.UsedDocuments);
        Assert.Single(response.Sources);

        // Verify prompt messages built by ChatService contained both ranking_snippet and answer_context
        OllamaChatMessage systemMsg = Assert.Single(ollama.LastMessages, m => m.Role == "system");
        Assert.Contains("<ranking_snippet>", systemMsg.Content);
        Assert.Contains("Ranking snippet match for query", systemMsg.Content);
        Assert.Contains("<answer_context>", systemMsg.Content);
        Assert.Contains("Full parent context section explaining all implementation details of SpecDoc.", systemMsg.Content);
        Assert.Contains("section=\"Section 5.1 Architecture\"", systemMsg.Content);
    }

    [Fact]
    public async Task SecurityPolicy_EndToEndIntegration_AuditsAndRestrictsWorkspace()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "OnlyRagE2ETest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            AppStoragePaths paths = AppStoragePaths.FromRoot(tempDir);
            var descriptor = new LocalSqliteStoreDescriptor(paths);

            var factory = new LocalSqliteConnectionFactory(descriptor);
            var initializer = new LocalSqliteSchemaInitializer(descriptor, factory);
            await initializer.InitializeAsync();

            var auditRepo = new SqlitePolicyAuditRepository(factory);
            var policyService = new AgentExecutionPolicyService(auditRepo);
            var taskManager = new BackgroundTaskManager();

            var executor = new WorkspaceToolExecutor(
                taskManager: taskManager,
                policyService: policyService);

            // 1. Allowed operation inside workspace
            string validArgs = JsonSerializer.Serialize(new { filePath = Path.Combine(tempDir, "sample.txt"), content = "Hello E2E" });
            var resSuccess = await executor.ExecuteToolAsync("call_ok", "write_file", validArgs, tempDir);
            Assert.True(resSuccess.Success);

            // 2. Disallowed operation outside workspace
            string invalidArgs = JsonSerializer.Serialize(new { filePath = "C:\\Windows\\System32\\forbidden.txt" });
            var resFail = await executor.ExecuteToolAsync("call_fail", "write_file", invalidArgs, tempDir);
            Assert.False(resFail.Success);
            Assert.Contains("Policy violation", resFail.Error);

            // 3. Verify audit log records
            var logs = await auditRepo.GetAuditLogsAsync();
            Assert.True(logs.Count >= 2);
            Assert.Contains(logs, l => l.CallId == "call_ok" && l.Allowed);
            Assert.Contains(logs, l => l.CallId == "call_fail" && !l.Allowed);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    private sealed class StaticRetrievalService(DocumentSearchResponse response) : IHybridRetrievalService
    {
        public Task<DocumentSearchResponse> SearchAsync(DocumentSearchRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(response);
        }
    }

    private sealed class InMemoryChatHistoryRepository : IChatHistoryRepository
    {
        public List<ChatHistoryRecord> Messages { get; } = [];

        public Task AppendMessageAsync(string conversationId, string role, string content, string? model = null, string? metadataJson = null, CancellationToken cancellationToken = default)
        {
            Messages.Add(new ChatHistoryRecord(Messages.Count + 1, conversationId, role, content, model, metadataJson, DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ChatHistoryRecord>> ListRecentMessagesAsync(string conversationId, int limit, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ChatHistoryRecord>>(Messages.Where(m => m.ConversationId == conversationId).TakeLast(limit).ToList());
        }

        public Task ClearHistoryAsync(string conversationId, CancellationToken cancellationToken = default)
        {
            Messages.Clear();
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

    private sealed class FakeOllamaClient(string installedModel, string answer) : IOllamaClient
    {
        public IReadOnlyList<OllamaChatMessage> LastMessages { get; private set; } = [];

        public Task ChatSmokeAsync(string modelName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteModelAsync(string modelName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EmbeddingsSmokeAsync(string modelName, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> GenerateChatAsync(
            string modelName,
            IReadOnlyList<OllamaChatMessage> messages,
            int? numCtx = null,
            object? format = null,
            object? tools = null,
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
            return Task.FromResult<IReadOnlyList<OllamaModelSummary>>([
                new OllamaModelSummary(installedModel, installedModel, null, 1, null, null, null, null)
            ]);
        }

        public Task PullModelAsync(string modelName, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task PullModelAsync(
            string modelName,
            Func<OllamaModelPullProgress, CancellationToken, Task> onProgress,
            CancellationToken cancellationToken = default)
        {
            return onProgress(new OllamaModelPullProgress("success", null, null, 100), cancellationToken);
        }

        public Task TestConnectionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
