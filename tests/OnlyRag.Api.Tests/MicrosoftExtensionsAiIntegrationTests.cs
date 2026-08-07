using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OnlyRag.Api;
using OnlyRag.Core;
using OnlyRag.Api.Services;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Ingestion;
using OnlyRag.Worker;
using Xunit;

namespace OnlyRag.Api.Tests;

public sealed class MicrosoftExtensionsAiIntegrationTests
{
    [Fact]
    public void ServiceRegistration_RegistersIChatClientAndIEmbeddingGenerator_WithLoggingPipelines()
    {
        var services = new ServiceCollection();
        string tempRoot = Path.Combine(Path.GetTempPath(), "OnlyRag.Tests", Guid.NewGuid().ToString("N"));

        try
        {
            var paths = AppStoragePaths.FromRoot(tempRoot);
            var store = new LocalSqliteStoreDescriptor(paths);
            var queue = LocalJobQueueDescriptor.Default;

            var descriptor = new InProcessBackendDescriptor(
                paths,
                store,
                queue,
                new OllamaEndpointOptions());

            var options = new InProcessBackendOptions();
            var runtimeState = new BackendRuntimeState(DateTimeOffset.UtcNow);

            services.AddOnlyRagBackendServices(descriptor, options, runtimeState);
            var provider = services.BuildServiceProvider();

            var chatClient = provider.GetService<IChatClient>();
            Assert.NotNull(chatClient);

            var embeddingGenerator = provider.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
            Assert.NotNull(embeddingGenerator);

            var streamingAdapter = provider.GetService<IStreamingEmbeddingGenerator>();
            Assert.NotNull(streamingAdapter);
            Assert.IsType<MicrosoftExtensionsAiEmbeddingGeneratorAdapter>(streamingAdapter);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task MicrosoftExtensionsAiEmbeddingGeneratorAdapter_GeneratesEmbeddings_UsingProvidedGenerator()
    {
        var mockGenerator = new TestEmbeddingGenerator();
        var adapter = new MicrosoftExtensionsAiEmbeddingGeneratorAdapter(mockGenerator);

        var result = await adapter.GenerateEmbeddingsAsync("test-model", new[] { "Hello world", "Test chunk" }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(4, result[0].Count);
    }

    private sealed class TestEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public void Dispose() { }

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var embeddings = values.Select(val => new Embedding<float>(new float[] { 0.1f, 0.2f, 0.3f, 0.4f })).ToList();
            var result = new GeneratedEmbeddings<Embedding<float>>(embeddings);
            return Task.FromResult(result);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => this;
    }
}
