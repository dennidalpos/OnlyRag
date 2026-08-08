using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ai;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Infrastructure.Storage.Security;

namespace OnlyRag.Api;

public static class InProcessBackendCloudLlmEndpoints
{
    private static CloudLlmConfiguration _currentConfig = new();

    public static CloudLlmConfiguration GetCurrentConfig() => Volatile.Read(ref _currentConfig);

    public static IEndpointRouteBuilder MapCloudLlmEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/settings/cloud-llm").WithTags("CloudLLM");

        group.MapGet("", async (ICloudApiKeyVault keyVault, CancellationToken ct) =>
        {
            string? apiKey = await keyVault.GetApiKeyAsync(_currentConfig.Provider, ct);
            var response = new CloudLlmSettingsResponse(
                Provider: _currentConfig.Provider,
                Endpoint: _currentConfig.Endpoint,
                ChatModel: _currentConfig.ChatModel,
                EmbeddingModel: _currentConfig.EmbeddingModel,
                DeploymentName: _currentConfig.DeploymentName,
                ApiVersion: _currentConfig.ApiVersion,
                HasApiKey: !string.IsNullOrWhiteSpace(apiKey));

            return Results.Ok(response);
        });

        group.MapPost("", async (UpdateCloudLlmSettingsRequest request, ICloudApiKeyVault keyVault, CancellationToken ct) =>
        {
            var newConfig = new CloudLlmConfiguration(
                Provider: request.Provider,
                Endpoint: request.Endpoint,
                ChatModel: request.ChatModel,
                EmbeddingModel: request.EmbeddingModel,
                DeploymentName: request.DeploymentName,
                ApiVersion: request.ApiVersion);
            Volatile.Write(ref _currentConfig, newConfig);

            if (!string.IsNullOrWhiteSpace(request.ApiKey))
            {
                await keyVault.SaveApiKeyAsync(request.Provider, request.ApiKey, ct);
            }

            string? apiKey = await keyVault.GetApiKeyAsync(_currentConfig.Provider, ct);
            var response = new CloudLlmSettingsResponse(
                Provider: _currentConfig.Provider,
                Endpoint: _currentConfig.Endpoint,
                ChatModel: _currentConfig.ChatModel,
                EmbeddingModel: _currentConfig.EmbeddingModel,
                DeploymentName: _currentConfig.DeploymentName,
                ApiVersion: _currentConfig.ApiVersion,
                HasApiKey: !string.IsNullOrWhiteSpace(apiKey));

            return Results.Ok(response);
        });

        group.MapPost("/test", async (UpdateCloudLlmSettingsRequest request, ICloudLlmClientFactory factory, ICloudApiKeyVault keyVault, CancellationToken ct) =>
        {
            string? apiKey = request.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = await keyVault.GetApiKeyAsync(request.Provider, ct);
            }

            var config = new CloudLlmConfiguration(
                Provider: request.Provider,
                Endpoint: request.Endpoint,
                ChatModel: request.ChatModel,
                EmbeddingModel: request.EmbeddingModel,
                DeploymentName: request.DeploymentName,
                ApiVersion: request.ApiVersion);

            var result = await factory.TestConnectionAsync(config, apiKey, ct);
            return Results.Ok(result);
        });

        return endpoints;
    }
}
