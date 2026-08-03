namespace OnlyRag.Core.Mcp;

public interface IMcpClientService
{
    Task<IReadOnlyList<McpServerConfig>> GetConfiguredServersAsync(CancellationToken cancellationToken = default);

    Task<McpServerConfig> RegisterServerAsync(McpServerConfig config, CancellationToken cancellationToken = default);

    Task UnregisterServerAsync(string serverId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<McpToolDescriptor>> GetAvailableToolsAsync(CancellationToken cancellationToken = default);

    Task<McpToolCallResponse> CallToolAsync(McpToolCallRequest request, CancellationToken cancellationToken = default);
}
