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
            Assert.Contains("Applied 2 replacement chunks", result.Output);

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
        Assert.Contains("[GIT LOCAL WORKSPACE STATUS:", result.Output);
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
            Assert.Contains("Document not found", result.Error);
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

    [Fact]
    public async Task ParallelToolCalls_UnpacksAndExecutesSubTools()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "OnlyRagToolTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string testFile = Path.Combine(tempDir, "sample.txt");
        await File.WriteAllTextAsync(testFile, "Hello World");

        try
        {
            var taskManager = new BackgroundTaskManager();
            var executor = new WorkspaceToolExecutor(taskManager);

            string argsJson = JsonSerializer.Serialize(new[]
            {
                new { tool = "read_file", arguments = new { relativePath = "sample.txt" } }
            });

            var result = await executor.ExecuteToolAsync("call_p1", "parallel_tool_calls", argsJson, tempDir);

            Assert.True(result.Success);
            Assert.Contains("Hello World", result.Output);
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
    public async Task MultiReplaceFileContent_HandlesArrayRelativePathParameter()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "OnlyRagToolTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string testFile = Path.Combine(tempDir, "array_test.txt");
        await File.WriteAllTextAsync(testFile, "Old text");

        try
        {
            var taskManager = new BackgroundTaskManager();
            var executor = new WorkspaceToolExecutor(taskManager);

            string argsJson = JsonSerializer.Serialize(new
            {
                relativePath = new[] { "array_test.txt" },
                chunks = new[]
                {
                    new { targetContent = "Old text", replacementContent = "New text" }
                }
            });

            var result = await executor.ExecuteToolAsync("call_arr", "multi_replace_file_content", argsJson, tempDir);

            Assert.True(result.Success);
            Assert.Equal("New text", await File.ReadAllTextAsync(testFile));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
