namespace OnlyRag.Core.Mcp;

public enum McpSseConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Reconnecting = 3,
    Failed = 4
}

public sealed record McpSseSessionStatus(
    string ServerId,
    McpSseConnectionState State,
    string? SessionId,
    string? PostEndpoint,
    string? LastError,
    DateTimeOffset? LastConnectedAt);

public interface IMcpSseClient
{
    Task<McpSseSessionStatus> ConnectAsync(McpServerConfig server, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<McpToolDescriptor>> FetchToolsAsync(McpServerConfig server, CancellationToken cancellationToken = default);

    Task<string> CallToolAsync(McpServerConfig server, string toolName, System.Text.Json.JsonElement arguments, CancellationToken cancellationToken = default);

    Task DisconnectAsync(string serverId, CancellationToken cancellationToken = default);

    McpSseSessionStatus GetSessionStatus(string serverId);
}
