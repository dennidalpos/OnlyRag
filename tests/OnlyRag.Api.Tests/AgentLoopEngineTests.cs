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
    [InlineData("<tool>\n{\n  \"name\": \"dir\",\n  \"parameters\": { \"relativePath\": \".\" }\n}\n</tool>", "list_dir")] // <tool> tag with dir alias
    [InlineData("```json\n{\n  \"type\": \"function\",\n  \"function\": {\n    \"name\": \"cat\",\n    \"arguments\": { \"relativePath\": \"README.md\" }\n  }\n}\n```", "read_file")] // OpenAI function object format with cat alias
    [InlineData("```json\n{\n  \"tool\": \"powershell\",\n  \"arguments\": \"{\\\"commandLine\\\": \\\"dotnet build\\\"}\"\n}\n```", "run_command")] // Stringified arguments with powershell alias
    [InlineData("Ecco la modifica:\n```json\n{\n  \"tool\": \"write_file\",\n  \"arguments\": {\n    \"relativePath\": \"test.cs\",\n    \"content\": \"using System;\nclass P {}\"\n  }\n}\n```", "write_file")] // Unescaped newlines in JSON string
    [InlineData("```json\n{\n  tool: 'list_dir',\n  arguments: { relativePath: '.' },\n}\n```", "list_dir")] // Malformed JSON (single quotes, unquoted keys, trailing comma)
    [InlineData("```json\n{\n  \"tool\": \"replace_file_content\",\n  \"arguments\": {\n    \"relativePath\": \"src\\OnlyFirmaOutlook\\Program.cs\",\n    \"targetContent\": \"class Program\",\n    \"replacementContent\": \"// \\M Comment\\nclass Program\"\n  }\n}\n```", "replace_file_content")] // Unescaped backslashes in Windows paths & C# comments
    [InlineData("```json\n{\n  \"tool\": \"write_file\",\n  \"arguments\": {\n    \"relativePath\": \"test.cs\",\n    \"content\": \"using System;\n\tclass P {}\"\n  }\n}\n```", "write_file")] // Literal tab character (0x09) in code string
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

    [Fact]
    public void EnrichGoalWithWorkspaceContext_InjectsWorkspaceMetadata()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "OnlyRagTestWorkspace_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "AGENTS.md"), "# Rules");
            File.WriteAllText(Path.Combine(tempDir, "workspace_settings.json"), "{\"preset\": \"vibe\"}");

            // Call internal EnrichGoalWithWorkspaceContext via reflection or method call
            var method = typeof(AgentLoopEngine).GetMethod("EnrichGoalWithWorkspaceContext", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(method);

            string result = (string)method.Invoke(null, new object[] { "Analizza il codice", tempDir })!;

            Assert.Contains("Analizza il codice", result);
            Assert.Contains("[CONTESTO WORKSPACE ATTIVO]", result);
            Assert.Contains("AGENTS.md", result);
            Assert.Contains("workspace_settings.json", result);
            Assert.Contains("{\"preset\": \"vibe\"}", result);
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
    public void IsCyclicPatternDetected_DetectsMetaToolSpinningAndCyclicPatterns()
    {
        var method = typeof(AgentLoopEngine).GetMethod("IsCyclicPatternDetected", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var historyMetaSpins = new List<string>
        {
            "reflect_step:{\"stepId\":\"1\"}",
            "plan_task:{\"steps\":[]}",
            "reflect_step:{\"stepId\":\"2\"}"
        };

        bool isMetaSpinDetected = (bool)method.Invoke(null, [historyMetaSpins])!;
        Assert.True(isMetaSpinDetected);

        var historyToolNameCycle = new List<string>
        {
            "list_dir:{\"relativePath\":\"a\"}",
            "read_file:{\"relativePath\":\"b\"}",
            "list_dir:{\"relativePath\":\"c\"}",
            "read_file:{\"relativePath\":\"d\"}",
            "list_dir:{\"relativePath\":\"e\"}",
            "read_file:{\"relativePath\":\"f\"}"
        };

        bool isToolNameCycleDetected = (bool)method.Invoke(null, [historyToolNameCycle])!;
        Assert.True(isToolNameCycleDetected);
    }

    [Fact]
    public void IsReadOnlyTool_Identifies_Independent_Tools()
    {
        var method = typeof(AgentLoopEngine).GetMethod("IsReadOnlyTool", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var callRead = new AgentToolCall("c1", "read_file", "{}");
        var callList = new AgentToolCall("c2", "list_dir", "{}");
        var callWrite = new AgentToolCall("c3", "write_file", "{}");
        var callCmd = new AgentToolCall("c4", "run_command", "{}");

        Assert.True((bool)method.Invoke(null, [callRead])!);
        Assert.True((bool)method.Invoke(null, [callList])!);
        Assert.False((bool)method.Invoke(null, [callWrite])!);
        Assert.False((bool)method.Invoke(null, [callCmd])!);
    }

    [Fact]
    public void AgentMemoryManager_CompressContext_SynthesizesHistory()
    {
        var memoryManager = new AgentMemoryManager();
        var messages = new List<OllamaChatMessage>
        {
            new("system", "System prompt"),
            new("user", "Goal")
        };

        for (int i = 0; i < 25; i++)
        {
            messages.Add(new("assistant", $"Thought step {i}"));
            messages.Add(new("user", $"[TOOL RESULT (tool_{i})]\nSuccesso: True\nOutput:\nResult {i}"));
        }

        bool compressed = memoryManager.CompressContext(messages, maxMessagesThreshold: 20);

        Assert.True(compressed);
        Assert.True(messages.Count < 20);
        Assert.Contains("[CONTESTO SINTETIZZATO DALL'AGENTE]", messages[2].Content);
    }
}

