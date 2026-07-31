using OnlyRag.Core;
using OnlyRag.Infrastructure.Retrieval;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public sealed class OllamaQueryTransformationServiceTests
{
    [Fact]
    public async Task TransformAsync_WithoutExpander_UsesHeuristicRules()
    {
        var service = new OllamaQueryTransformationService();
        var result = await service.TransformAsync("Come configurare l'API di RAG?", QueryTransformationStrategy.MultiQuery);

        Assert.Equal(QueryTransformationStrategy.MultiQuery, result.Strategy);
        Assert.True(result.ExpandedQueries.Count >= 2);
        Assert.Contains("configurare l'API di RAG?", result.ExpandedQueries);
    }

    [Fact]
    public async Task TransformAsync_WithMockLlmExpander_AppendsLlmVariants()
    {
        var mockExpander = new FakeLlmQueryExpander("1. RAG API configuration guide\n2. Setup retrieval augmented generation");
        var service = new OllamaQueryTransformationService(mockExpander);

        var result = await service.TransformAsync("RAG API config", QueryTransformationStrategy.MultiQuery);

        Assert.Contains("RAG API configuration guide", result.ExpandedQueries);
        Assert.Contains("Setup retrieval augmented generation", result.ExpandedQueries);
    }

    [Fact]
    public async Task TransformAsync_WhenLlmExpanderFails_FallsBackToHeuristicsSilently()
    {
        var failingExpander = new FailingLlmQueryExpander();
        var service = new OllamaQueryTransformationService(failingExpander);

        var result = await service.TransformAsync("Database vector storage", QueryTransformationStrategy.HyDE);

        Assert.Equal(QueryTransformationStrategy.HyDE, result.Strategy);
        Assert.Single(result.ExpandedQueries);
        Assert.Contains("Database vector storage", result.ExpandedQueries);
    }

    [Fact]
    public async Task TransformAsync_CachesLlmVariants_DoesNotReinvokeExpander()
    {
        var mockExpander = new CountingFakeLlmQueryExpander("1. Cached variant 1\n2. Cached variant 2");
        var service = new OllamaQueryTransformationService(mockExpander);

        var firstResult = await service.TransformAsync("Vector search query", QueryTransformationStrategy.MultiQuery);
        var secondResult = await service.TransformAsync("Vector search query", QueryTransformationStrategy.MultiQuery);

        Assert.Equal(1, mockExpander.CallCount);
        Assert.Contains("Cached variant 1", firstResult.ExpandedQueries);
        Assert.Contains("Cached variant 1", secondResult.ExpandedQueries);
    }

    private sealed class CountingFakeLlmQueryExpander : ILlmQueryExpander
    {
        private readonly string response;
        public int CallCount { get; private set; }

        public CountingFakeLlmQueryExpander(string response)
        {
            this.response = response;
        }

        public Task<string?> GenerateExpansionAsync(string prompt, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<string?>(response);
        }
    }

    private sealed class FakeLlmQueryExpander : ILlmQueryExpander
    {
        private readonly string response;

        public FakeLlmQueryExpander(string response)
        {
            this.response = response;
        }

        public Task<string?> GenerateExpansionAsync(string prompt, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(response);
        }
    }

    private sealed class FailingLlmQueryExpander : ILlmQueryExpander
    {
        public Task<string?> GenerateExpansionAsync(string prompt, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Ollama service unreachable");
        }
    }
}
