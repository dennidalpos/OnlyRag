using System.Text.Json;
using OnlyRag.Infrastructure.Agent;
using OnlyRag.Infrastructure.Agent.Tools;
using Xunit;

namespace OnlyRag.Api.Tests;

public sealed class TaskAndCommandToolHandlerTests
{
    [Theory]
    [InlineData("start file.txt")]
    [InlineData("start-process explorer.exe")]
    [InlineData("explorer .")]
    [InlineData("notepad log.txt")]
    [InlineData("code src/Program.cs")]
    [InlineData("invoke-item document.pdf")]
    [InlineData("ii readme.md")]
    [InlineData("open http://localhost:5000")]
    [InlineData("cmd /c start file.txt")]
    public async Task RunCommand_RejectsGuiFileOpenCommands(string commandLine)
    {
        var taskManager = new BackgroundTaskManager();
        var handler = new TaskAndCommandToolHandler(taskManager);

        using var doc = JsonDocument.Parse($"{{\"commandLine\": \"{commandLine}\"}}");
        var result = await handler.ExecuteAsync("call_1", "run_command", doc.RootElement, Directory.GetCurrentDirectory());

        Assert.False(result.Success);
        Assert.Contains("GUI file opening commands", result.Error);
        Assert.Contains("disabled", result.Error);
    }

    [Fact]
    public async Task RunCommand_ExecutesInternalCliCommandSuccessfully()
    {
        var taskManager = new BackgroundTaskManager();
        var handler = new TaskAndCommandToolHandler(taskManager);

        using var doc = JsonDocument.Parse("{\"commandLine\": \"Write-Host 'Hello Internal CLI'\"}");
        var result = await handler.ExecuteAsync("call_2", "run_command", doc.RootElement, Directory.GetCurrentDirectory());

        Assert.True(result.Success);
        Assert.Contains("Hello Internal CLI", result.Output);
        Assert.Null(result.Error);
    }
}
