using System.Text.Json;
using System.Text.Json.Serialization;

namespace OnlyRag.Core.Mcp;

public sealed record McpToolDescriptor(
    [property: JsonPropertyName("serverId")] string ServerId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("inputSchema")] JsonElement InputSchema);

public sealed record McpToolCallRequest(
    [property: JsonPropertyName("serverId")] string ServerId,
    [property: JsonPropertyName("toolName")] string ToolName,
    [property: JsonPropertyName("arguments")] JsonElement Arguments);

public sealed record McpToolCallResponse(
    [property: JsonPropertyName("isSuccess")] bool IsSuccess,
    [property: JsonPropertyName("output")] string Output,
    [property: JsonPropertyName("error")] string? Error = null);
