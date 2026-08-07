namespace OnlyRag.Core;

public enum CloudLlmProvider
{
    OllamaLocal = 0,
    AzureOpenAi = 1,
    OpenAi = 2,
    Anthropic = 3,
    GoogleGemini = 4
}

public sealed record CloudLlmConfiguration(
    CloudLlmProvider Provider = CloudLlmProvider.OllamaLocal,
    string Endpoint = "",
    string ChatModel = "nomic-embed-text",
    string EmbeddingModel = "nomic-embed-text",
    string DeploymentName = "",
    string ApiVersion = "2024-02-15-preview");

public sealed record CloudLlmSettingsResponse(
    CloudLlmProvider Provider,
    string Endpoint,
    string ChatModel,
    string EmbeddingModel,
    string DeploymentName,
    string ApiVersion,
    bool HasApiKey);

public sealed record UpdateCloudLlmSettingsRequest(
    CloudLlmProvider Provider,
    string Endpoint,
    string ChatModel,
    string EmbeddingModel,
    string DeploymentName,
    string ApiVersion,
    string? ApiKey);

public sealed record CloudLlmTestResult(
    bool Success,
    string Message,
    long LatencyMs);
