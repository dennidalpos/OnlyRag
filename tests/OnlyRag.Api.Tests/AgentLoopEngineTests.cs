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
    [InlineData("```json\n{\n  \"tool\": \"web_search\",\n  \"arguments\": { \"query\": \"C# 13 features\", \"domain\": \"learn.microsoft.com\" }\n}\n```", "web_search")] // Web search tool
    [InlineData("```json\n{\n  \"tool\": \"search_web\",\n  \"arguments\": { \"query\": \"React 19 hooks\" }\n}\n```", "web_search")] // Web search alias
    [InlineData("<tool_call>\n{\n  \"name\": \"list_dir\",\n  \"inputs\": { \"relativePath\": \".\" }\n}\n</tool_call>", "list_dir")] // Open source <tool_call> tag format
    [InlineData("Ecco la modifica:\n```json\n{\n  \"tool\": \"write_file\",\n  \"arguments\": {\n    \"relativePath\": \"test.cs\",\n    \"content\": \"using System;\nclass P {}\"\n  }\n}\n```", "write_file")] // Unescaped newlines in JSON string
    [InlineData("```json\n{\n  tool: 'list_dir',\n  arguments: { relativePath: '.' },\n}\n```", "list_dir")] // Malformed JSON (single quotes, unquoted keys, trailing comma)
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

    [Fact]
    public void AgentRunRequest_SupportsNullAndCustomMaxIterations()
    {
        var requestUnlimited = new AgentRunRequest("Refactor code");
        var requestCustom = new AgentRunRequest("Refactor code", MaxIterations: 50);

        Assert.Null(requestUnlimited.MaxIterations);
        Assert.Equal(50, requestCustom.MaxIterations);
    }
}
