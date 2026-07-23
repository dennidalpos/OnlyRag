using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using Xunit;

namespace OnlyRag.Api.Tests;

public sealed class CodingServiceTests
{
    [Fact]
    public async Task GenerateCodeAsync_ReturnsExtractedCodeAndExplanation()
    {
        string mockLlmOutput = "Ecco il codice che hai chiesto:\n```csharp\npublic class Calculator { public int Add(int a, int b) => a + b; }\n```\nQuesta classe esegue la somma.";
        FakeOllamaClient ollama = new("qwen2.5-coder", mockLlmOutput);
        StubOllamaSettingsService settingsService = new();

        CodingService service = new(ollama, settingsService);

        CodingTaskResponse response = await service.GenerateCodeAsync(new CodingTaskRequest(
            Prompt: "Crea una classe Calculator in C#",
            Model: "qwen2.5-coder",
            Persona: "architect",
            Language: "csharp"));

        Assert.Equal("public class Calculator { public int Add(int a, int b) => a + b; }", response.GeneratedCode);
        Assert.Equal("csharp", response.Language);
        Assert.Contains("Questa classe esegue la somma.", response.Explanation);
    }

    [Fact]
    public async Task RefactorCodeAsync_ReturnsRefactoredSnippet()
    {
        string mockLlmOutput = "```typescript\nconst add = (a: number, b: number): number => a + b;\n```\nConvertito in arrow function fortemente tipizzata.";
        FakeOllamaClient ollama = new("qwen2.5-coder", mockLlmOutput);
        StubOllamaSettingsService settingsService = new();

        CodingService service = new(ollama, settingsService);

        CodeRefactorResponse response = await service.RefactorCodeAsync(new CodeRefactorRequest(
            OriginalCode: "function add(a, b) { return a + b; }",
            Goal: "type_safety",
            Language: "typescript"));

        Assert.Equal("function add(a, b) { return a + b; }", response.OriginalCode);
        Assert.Equal("const add = (a: number, b: number): number => a + b;", response.ModifiedCode);
        Assert.Equal("typescript", response.Language);
    }

    [Fact]
    public async Task DiagnoseCodeAsync_ReturnsAnalysisAndFixedCode()
    {
        string mockLlmOutput = "### Analisi Causa Radice\nNullReferenceException dovuta al mancato controllo di valori null.\n\n### Codice Corretto\n```csharp\nif (item != null) { item.DoWork(); }\n```";
        FakeOllamaClient ollama = new("qwen2.5-coder", mockLlmOutput);
        StubOllamaSettingsService settingsService = new();

        CodingService service = new(ollama, settingsService);

        CodeDiagnoseResponse response = await service.DiagnoseCodeAsync(new CodeDiagnoseRequest(
            ErrorLog: "System.NullReferenceException: Object reference not set to an instance of an object.",
            CodeContext: "item.DoWork();",
            Language: "csharp"));

        Assert.Contains("NullReferenceException", response.RootCauseAnalysis);
        Assert.Equal("if (item != null) { item.DoWork(); }", response.SuggestedFixCode);
        Assert.Equal("csharp", response.Language);
    }

    private sealed class FakeOllamaClient : IOllamaClient
    {
        private readonly string expectedModel;
        private readonly string answerToReturn;

        public List<OllamaChatMessage> LastMessages { get; } = [];

        public FakeOllamaClient(string expectedModel, string answerToReturn)
        {
            this.expectedModel = expectedModel;
            this.answerToReturn = answerToReturn;
        }

        public Task TestConnectionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<OllamaModelSummary>> ListModelsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<OllamaModelSummary>>([
                new OllamaModelSummary(expectedModel, expectedModel, DateTimeOffset.UtcNow, 4_000_000_000, "sha256:fake", "qwen2", "7B", "Q4_0")
            ]);
        }

        public Task PullModelAsync(string modelName, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task PullModelAsync(string modelName, Func<OllamaModelPullProgress, CancellationToken, Task> onProgress, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteModelAsync(string modelName, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ChatSmokeAsync(string modelName, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task EmbeddingsSmokeAsync(string modelName, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> GenerateChatAsync(string modelName, IReadOnlyList<OllamaChatMessage> messages, int? numCtx = null, CancellationToken cancellationToken = default)
        {
            LastMessages.Clear();
            LastMessages.AddRange(messages);
            return Task.FromResult(answerToReturn);
        }

        public Task<OllamaModelDetails> ShowModelAsync(string modelName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new OllamaModelDetails(modelName, 8192));
        }

        public Task<IReadOnlyList<IReadOnlyList<float>>> GenerateEmbeddingsAsync(string modelName, IReadOnlyList<string> inputs, int? numCtx = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<IReadOnlyList<float>>>([new float[] { 0.1f, 0.2f }]);
        }
    }

    private sealed class StubOllamaSettingsService : IOllamaSettingsService
    {
        public Task<OllamaSettings> GetAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new OllamaSettings(
                "http://127.0.0.1:11434",
                "qwen2.5-coder",
                null,
                null,
                60,
                1));
        }

        public Task<OllamaSettings> UpdateAsync(OllamaSettings settings, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task ClearMissingDefaultModelAsync(string modelName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
