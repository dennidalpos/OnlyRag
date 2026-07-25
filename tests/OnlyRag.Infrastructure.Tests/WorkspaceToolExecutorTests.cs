using System.Text.Json;
using OnlyRag.Infrastructure.Agent;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public sealed class WorkspaceToolExecutorTests
{
    [Fact]
    public async Task MultiReplaceFileContent_AppliesMultipleChunksSuccessfully()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "OnlyRagToolTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string testFile = Path.Combine(tempDir, "test.txt");
        await File.WriteAllTextAsync(testFile, "Alpha\nBeta\nGamma\nDelta");

        try
        {
            var taskManager = new BackgroundTaskManager();
            var executor = new WorkspaceToolExecutor(taskManager);

            string argsJson = JsonSerializer.Serialize(new
            {
                relativePath = "test.txt",
                chunks = new[]
                {
                    new { targetContent = "Alpha", replacementContent = "ALPHA_NEW" },
                    new { targetContent = "Gamma", replacementContent = "GAMMA_NEW" }
                }
            });

            var result = await executor.ExecuteToolAsync("call_1", "multi_replace_file_content", argsJson, tempDir);

            Assert.True(result.Success);
            Assert.Contains("Applicati 2 chunk", result.Output);

            string updatedText = await File.ReadAllTextAsync(testFile);
            Assert.Contains("ALPHA_NEW", updatedText);
            Assert.Contains("GAMMA_NEW", updatedText);
            Assert.Contains("Beta", updatedText);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task GitDiffInspect_ExecutesWithoutError()
    {
        string rootPath = Directory.GetCurrentDirectory();
        var taskManager = new BackgroundTaskManager();
        var executor = new WorkspaceToolExecutor(taskManager);

        var result = await executor.ExecuteToolAsync("call_2", "git_diff_inspect", "{}", rootPath);

        Assert.True(result.Success);
        Assert.Contains("[STATO GIT LOCAL WORKSPACE:", result.Output);
    }

    [Fact]
    public async Task IngestOfficeDoc_HandlesMissingFileGracefully()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "OnlyRagToolTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var taskManager = new BackgroundTaskManager();
            var executor = new WorkspaceToolExecutor(taskManager);

            string argsJson = JsonSerializer.Serialize(new { relativePath = "missing.docx" });
            var result = await executor.ExecuteToolAsync("call_3", "ingest_office_doc", argsJson, tempDir);

            Assert.False(result.Success);
            Assert.Contains("Documento non trovato", result.Error);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task GenerateImageOnnx_ReturnsStatusResponse()
    {
        var taskManager = new BackgroundTaskManager();
        var executor = new WorkspaceToolExecutor(taskManager);

        string argsJson = JsonSerializer.Serialize(new { prompt = "Cyberpunk neon city skyline" });
        var result = await executor.ExecuteToolAsync("call_4", "generate_image_onnx", argsJson, Directory.GetCurrentDirectory());

        Assert.True(result.Success);
        Assert.Contains("Cyberpunk neon city skyline", result.Output);
    }

    [Fact]
    public async Task QueryRetrievalIndex_ReturnsStatusResponse()
    {
        var taskManager = new BackgroundTaskManager();
        var executor = new WorkspaceToolExecutor(taskManager);

        string argsJson = JsonSerializer.Serialize(new { query = "Vector RAG retrieval", topK = 3 });
        var result = await executor.ExecuteToolAsync("call_5", "query_retrieval_index", argsJson, Directory.GetCurrentDirectory());

        Assert.True(result.Success);
        Assert.Contains("Vector RAG retrieval", result.Output);
    }
}
