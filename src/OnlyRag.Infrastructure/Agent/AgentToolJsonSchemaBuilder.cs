using System.Text.Json.Nodes;

namespace OnlyRag.Infrastructure.Agent;

/// <summary>
/// Constructs JSON Schema objects suitable for Ollama's format parameter to strictly enforce
/// structured tool call outputs from local LLMs.
/// </summary>
public static class AgentToolJsonSchemaBuilder
{
    private static readonly string[] AllowedTools =
    [
        "read_file",
        "write_file",
        "replace_file_content",
        "multi_replace_file_content",
        "apply_diff_patch",
        "run_command",
        "list_dir",
        "grep_search",
        "git_diff_inspect",
        "web_search",
        "ingest_office_doc",
        "query_retrieval_index",
        "plan_task",
        "reflect_step",
        "manage_task",
        "ast_structural_refactor",
        "refactor_symbol",
        "ask_question",
        "define_subagent",
        "invoke_subagent",
        "send_message"
    ];

    /// <summary>
    /// Returns the JSON Schema object definition for single and batch tool calls, enforcing tool name enums.
    /// </summary>
    public static JsonObject BuildToolCallJsonSchema()
    {
        var toolEnumArray = new JsonArray();
        foreach (string toolName in AllowedTools)
        {
            toolEnumArray.Add(toolName);
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["tool"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = toolEnumArray
                },
                ["arguments"] = new JsonObject
                {
                    ["type"] = "object"
                },
                ["explanation"] = new JsonObject
                {
                    ["type"] = "string"
                }
            },
            ["required"] = new JsonArray { "tool", "arguments" }
        };
    }
}
