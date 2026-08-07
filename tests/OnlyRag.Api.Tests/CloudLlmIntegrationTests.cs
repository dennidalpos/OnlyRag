using Microsoft.Extensions.AI;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ai;
using OnlyRag.Infrastructure.Storage.Security;
using Xunit;

namespace OnlyRag.Api.Tests;

public sealed class CloudLlmIntegrationTests
{
    [Fact]
    public void CloudLlmClientFactory_CreatesChatClients_ForSupportedProviders()
    {
        var factory = new CloudLlmClientFactory();

        var azureConfig = new CloudLlmConfiguration(CloudLlmProvider.AzureOpenAi, "https://test.openai.azure.com", "gpt-4o", "text-embedding-3-large");
        var openAiConfig = new CloudLlmConfiguration(CloudLlmProvider.OpenAi, "https://api.openai.com/v1", "gpt-4o-mini", "text-embedding-3-small");
        var anthropicConfig = new CloudLlmConfiguration(CloudLlmProvider.Anthropic, "https://api.anthropic.com/v1", "claude-3-5-sonnet-20241022", "");
        var geminiConfig = new CloudLlmConfiguration(CloudLlmProvider.GoogleGemini, "https://generativelanguage.googleapis.com/v1beta", "gemini-1.5-flash", "text-embedding-004");
        var ollamaConfig = new CloudLlmConfiguration(CloudLlmProvider.OllamaLocal, "http://localhost:11434", "llama3", "nomic-embed-text");

        IChatClient azureClient = factory.CreateChatClient(azureConfig, "fake-key");
        IChatClient openAiClient = factory.CreateChatClient(openAiConfig, "fake-key");
        IChatClient anthropicClient = factory.CreateChatClient(anthropicConfig, "fake-key");
        IChatClient geminiClient = factory.CreateChatClient(geminiConfig, "fake-key");
        IChatClient ollamaClient = factory.CreateChatClient(ollamaConfig, null);

        Assert.NotNull(azureClient);
        Assert.NotNull(openAiClient);
        Assert.NotNull(anthropicClient);
        Assert.NotNull(geminiClient);
        Assert.NotNull(ollamaClient);
    }

    [Fact]
    public void CloudLlmClientFactory_CreatesEmbeddingGenerators_ForSupportedProviders()
    {
        var factory = new CloudLlmClientFactory();

        var azureConfig = new CloudLlmConfiguration(CloudLlmProvider.AzureOpenAi, "https://test.openai.azure.com", "gpt-4o", "text-embedding-3-large");
        var geminiConfig = new CloudLlmConfiguration(CloudLlmProvider.GoogleGemini, "https://generativelanguage.googleapis.com/v1beta", "gemini-1.5-flash", "text-embedding-004");

        var azureEmbedder = factory.CreateEmbeddingGenerator(azureConfig, "fake-key");
        var geminiEmbedder = factory.CreateEmbeddingGenerator(geminiConfig, "fake-key");

        Assert.NotNull(azureEmbedder);
        Assert.NotNull(geminiEmbedder);
    }

    [Fact]
    public async Task KeyVault_SavesAndRetrievesApiKey_Successfully()
    {
        var vault = new WindowsCredentialManagerCloudKeyVault();
        string testKey = "sk-test-key-123456789";

        await vault.SaveApiKeyAsync(CloudLlmProvider.Anthropic, testKey);
        string? retrievedKey = await vault.GetApiKeyAsync(CloudLlmProvider.Anthropic);

        Assert.Equal(testKey, retrievedKey);

        await vault.DeleteApiKeyAsync(CloudLlmProvider.Anthropic);
    }
}
