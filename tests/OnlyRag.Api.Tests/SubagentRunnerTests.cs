using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Agent;
using OnlyRag.Infrastructure.Logging;
using Xunit;

namespace OnlyRag.Api.Tests;

public sealed class SubagentRunnerTests
{
    [Fact]
    public async Task InvokeSubagentAsync_ParsesSingleSubagentSpec_AndRunsEngine()
    {
        var services = new ServiceCollection();
        var mockOllama = new TestOllamaClient();
        services.AddSingleton<IOllamaClient>(mockOllama);
        services.AddSingleton<IOllamaSettingsService>(new FakeOllamaSettingsService());
        services.AddSingleton<BackgroundTaskManager>();
        services.AddSingleton<ISubagentRunner, SubagentRunner>();
        services.AddSingleton<WorkspaceToolExecutor>();
        services.AddTransient<AgentLoopEngine>();

        var provider = services.BuildServiceProvider();
        var runner = provider.GetRequiredService<ISubagentRunner>();

        string tempDir = Path.Combine(Path.GetTempPath(), "OnlyRagSubagentTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                role = "CodeReviewer",
                prompt = "Review the codebase architecture and security.",
                max_iterations = 3
            }));

            var result = await runner.InvokeSubagentAsync("call_sub_1", "invoke_subagent", doc.RootElement, tempDir);

            Assert.True(result.Success);
            Assert.Contains("[SUBAGENT OUTPUT: CodeReviewer]", result.Output);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task InvokeSubagentAsync_ParsesMultipleSubagents_AndRunsConcurrently()
    {
        var services = new ServiceCollection();
        var mockOllama = new TestOllamaClient();
        services.AddSingleton<IOllamaClient>(mockOllama);
        services.AddSingleton<IOllamaSettingsService>(new FakeOllamaSettingsService());
        services.AddSingleton<BackgroundTaskManager>();
        services.AddSingleton<ISubagentRunner, SubagentRunner>();
        services.AddSingleton<WorkspaceToolExecutor>();
        services.AddTransient<AgentLoopEngine>();

        var provider = services.BuildServiceProvider();
        var runner = provider.GetRequiredService<ISubagentRunner>();

        string tempDir = Path.Combine(Path.GetTempPath(), "OnlyRagSubagentTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                subagents = new[]
                {
                    new { role = "FrontendAuditor", prompt = "Inspect React components.", max_iterations = 2 },
                    new { role = "BackendAuditor", prompt = "Inspect Minimal API endpoints.", max_iterations = 2 }
                }
            }));

            var result = await runner.InvokeSubagentAsync("call_sub_2", "invoke_subagent", doc.RootElement, tempDir);

            Assert.True(result.Success);
            Assert.Contains("FrontendAuditor", result.Output);
            Assert.Contains("BackendAuditor", result.Output);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    private sealed class TestOllamaClient : IOllamaClient
    {
        public Task TestConnectionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<OllamaModelSummary>> ListModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OllamaModelSummary>>([new OllamaModelSummary("qwen2.5-coder:7b", "qwen2.5-coder:7b", DateTimeOffset.UtcNow, 100, "sha256", "qwen", "7B", "Q4_K_M")]);

        public Task PullModelAsync(string modelName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PullModelAsync(string modelName, Func<OllamaModelPullProgress, CancellationToken, Task> onProgress, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteModelAsync(string modelName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ChatSmokeAsync(string modelName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EmbeddingsSmokeAsync(string modelName, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> GenerateChatAsync(string modelName, IReadOnlyList<OllamaChatMessage> messages, int? numCtx = null, object? format = null, object? tools = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult("Subagent completed task successfully.");
        }

        public async IAsyncEnumerable<string> GenerateChatStreamAsync(string modelName, IReadOnlyList<OllamaChatMessage> messages, int? numCtx = null, object? format = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return "Subagent completed task successfully.";
            await Task.Yield();
        }

        public Task<OllamaModelDetails> ShowModelAsync(string modelName, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OllamaModelDetails("qwen2.5-coder:7b", 4096));

        public Task<IReadOnlyList<IReadOnlyList<float>>> GenerateEmbeddingsAsync(string modelName, IReadOnlyList<string> inputs, int? numCtx = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IReadOnlyList<float>>>([[0.1f, 0.2f]]);
    }

    private sealed class FakeOllamaSettingsService : IOllamaSettingsService
    {
        public Task<OllamaSettings> GetAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new OllamaSettings(
                OllamaBaseUrl: "http://localhost:11434",
                DefaultChatModel: "qwen2.5-coder:7b",
                DefaultEmbeddingModel: "bge-m3:latest",
                DefaultTranslationModel: null,
                RequestTimeoutSeconds: 30,
                EmbeddingBatchSize: 16));
        }

        public Task<OllamaSettings> UpdateAsync(OllamaSettings settings, CancellationToken cancellationToken = default) => Task.FromResult(settings);

        public Task ClearMissingDefaultModelAsync(string modelName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
