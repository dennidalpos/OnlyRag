using System.Text.Json.Nodes;

namespace OnlyRag.Infrastructure.Agent;

/// <summary>
/// Constructs JSON Schema objects suitable for Ollama's format parameter to strictly enforce
/// structured tool call outputs from local LLMs.
/// </summary>
public static class AgentToolJsonSchemaBuilder
{
    /// <summary>
    /// Returns the JSON Schema object definition for single and batch tool calls.
    /// </summary>
    public static JsonObject BuildToolCallJsonSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["tool"] = new JsonObject { ["type"] = "string" },
                ["arguments"] = new JsonObject { ["type"] = "object" },
                ["explanation"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "tool", "arguments" }
        };
    }
}
