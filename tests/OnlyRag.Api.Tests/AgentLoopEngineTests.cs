using OnlyRag.Core;
using Xunit;

namespace OnlyRag.Api.Tests;

public sealed class AgentLoopEngineTests
{
    [Theory]
    [InlineData("```json\n{\n  \"tool\": \"list_dir\",\n  \"arguments\": { \"relativePath\": \".\" }\n}\n```", "list_dir")]
    [InlineData("```JSON\n{\n  \"tool\": \"read_file\",\n  \"arguments\": { \"relativePath\": \"src/Program.cs\" }\n}\n```", "read_file")]
    [InlineData("```json\n{\n  \"tool\": \"grep_search\",\n  \"arguments\": { \"query\": \"AgentLoopEngine\" }\n}", "grep_search")] // Truncated / unclosed code block
    [InlineData("Analizzo la struttura del progetto:\n{\"tool\": \"list_dir\", \"arguments\": {\"relativePath\": \"src\"}}", "list_dir")] // Embedded JSON without backticks
    [InlineData("```json\n{\n  \"action\": \"readfile\",\n  \"args\": { \"relativePath\": \"README.md\" }\n}\n```", "read_file")] // Alias readfile & action key
    [InlineData("```json\n{\n  \"function\": \"exec\",\n  \"parameters\": { \"commandLine\": \"dotnet test\" }\n}\n```", "run_command")] // Alias exec & function key
    [InlineData("```json\n{\n  \"tool_name\": \"ls\",\n  \"arguments\": { \"relativePath\": \".\" }\n}\n```", "list_dir")] // Alias ls & tool_name key
    public void TryExtractToolCall_ParsesVariousValidToolCalls(string llmOutput, string expectedToolName)
    {
        AgentToolCall? toolCall = AgentLoopEngine.TryExtractToolCall(llmOutput);

        Assert.NotNull(toolCall);
        Assert.Equal(expectedToolName, toolCall.ToolName);
    }

    [Fact]
    public void TryExtractToolCall_ReturnsNull_ForPlainMarkdownResponse()
    {
        string llmOutput = "Ho analizzato i file e completato l'implementazione richiesta. Tutti i test passano.";

        AgentToolCall? toolCall = AgentLoopEngine.TryExtractToolCall(llmOutput);

        Assert.Null(toolCall);
    }
}
