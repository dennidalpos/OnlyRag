using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using OnlyRag.Core.Mcp;

namespace OnlyRag.Api;

public static class InProcessBackendMcpEndpoints
{
    public static IEndpointRouteBuilder MapMcpEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/mcp").WithTags("MCP");

        group.MapGet("/servers", async (IMcpClientService mcpService, CancellationToken ct) =>
        {
            var servers = await mcpService.GetConfiguredServersAsync(ct);
            return Results.Ok(servers);
        });

        group.MapPost("/servers", async (McpServerConfig config, IMcpClientService mcpService, CancellationToken ct) =>
        {
            var saved = await mcpService.RegisterServerAsync(config, ct);
            return Results.Ok(saved);
        });

        group.MapDelete("/servers/{serverId}", async (string serverId, IMcpClientService mcpService, CancellationToken ct) =>
        {
            await mcpService.UnregisterServerAsync(serverId, ct);
            return Results.NoContent();
        });

        group.MapGet("/tools", async (IMcpClientService mcpService, CancellationToken ct) =>
        {
            var tools = await mcpService.GetAvailableToolsAsync(ct);
            return Results.Ok(tools);
        });

        group.MapPost("/tools/call", async (McpToolCallRequest request, IMcpClientService mcpService, CancellationToken ct) =>
        {
            var result = await mcpService.CallToolAsync(request, ct);
            return result.IsSuccess ? Results.Ok(result) : Results.BadRequest(result);
        });

        return endpoints;
    }
}
