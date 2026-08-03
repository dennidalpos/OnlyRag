using System.Text.Json.Serialization;

namespace OnlyRag.Core.Mcp;

public enum McpTransportType
{
    Stdio = 0,
    HttpSse = 1
}

public sealed record McpServerConfig(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("transport")] McpTransportType Transport,
    [property: JsonPropertyName("command")] string? Command,
    [property: JsonPropertyName("arguments")] IReadOnlyList<string>? Arguments,
    [property: JsonPropertyName("environmentVariables")] IReadOnlyDictionary<string, string>? EnvironmentVariables,
    [property: JsonPropertyName("serverUrl")] string? ServerUrl,
    [property: JsonPropertyName("isEnabled")] bool IsEnabled = true);
