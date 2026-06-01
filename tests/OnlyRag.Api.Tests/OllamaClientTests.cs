using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;

namespace OnlyRag.Api.Tests;

public sealed class OllamaClientTests
{
    [Theory]
    [InlineData("http://localhost:11434")]
    [InlineData("http://127.0.0.1:11434")]
    [InlineData("http://[::1]:11434")]
    public void NormalizeAndValidateBaseUrl_AllowsLoopbackByDefault(string baseUrl)
    {
        string normalized = OllamaSettingsService.NormalizeAndValidateBaseUrl(baseUrl);

        Assert.StartsWith("http://", normalized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalizeAndValidateBaseUrl_RejectsNonLoopbackWithoutTrust()
    {
        OllamaApiException exception = Assert.Throws<OllamaApiException>(
            () => OllamaSettingsService.NormalizeAndValidateBaseUrl("http://192.168.1.40:11434"));

        Assert.Equal(OllamaErrorKind.InvalidUrl, exception.Kind);
        Assert.Contains("conferma esplicita", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalizeAndValidateBaseUrl_AllowsNonLoopbackWithTrust()
    {
        string normalized = OllamaSettingsService.NormalizeAndValidateBaseUrl(
            "http://192.168.1.40:11434",
            trustNonLocalEndpoint: true);

        Assert.Equal("http://192.168.1.40:11434", normalized);
    }

    [Theory]
    [InlineData("ftp://localhost:11434")]
    [InlineData("http://")]
    [InlineData("http://localhost:11434?token=abc")]
    public void NormalizeAndValidateBaseUrl_RejectsUnsupportedOrAmbiguousUrls(string baseUrl)
    {
        OllamaApiException exception = Assert.Throws<OllamaApiException>(
            () => OllamaSettingsService.NormalizeAndValidateBaseUrl(baseUrl, trustNonLocalEndpoint: true));

        Assert.Equal(OllamaErrorKind.InvalidUrl, exception.Kind);
    }

    [Fact]
    public async Task ListModelsAsync_ReturnsInstalledModels()
    {
        StubHttpMessageHandler handler = new((request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("http://localhost:11434/api/tags", request.RequestUri?.ToString());

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    models = new[]
                    {
                        new
                        {
                            name = "gemma3:4b",
                            model = "gemma3:4b",
                            modified_at = "2026-04-25T00:00:00Z",
                            size = 3338801804L,
                            digest = "sha256",
                            details = new
                            {
                                family = "gemma",
                                parameter_size = "4.3B",
                                quantization_level = "Q4_K_M"
                            }
                        }
                    }
                })
            });
        });

        OllamaClient client = CreateClient(handler);

        IReadOnlyList<OllamaModelSummary> models = await client.ListModelsAsync();

        Assert.Single(models);
        Assert.Equal("gemma3:4b", models[0].Name);
        Assert.Equal("gemma", models[0].Family);
        Assert.Equal("4.3B", models[0].ParameterSize);
    }

    [Fact]
    public async Task PullModelAsync_SendsModelNameAndNonStreamingFlag()
    {
        StubHttpMessageHandler handler = new(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("http://localhost:11434/api/pull", request.RequestUri?.ToString());

            PullRequestBody? body = await request.Content!.ReadFromJsonAsync<PullRequestBody>(cancellationToken);
            Assert.NotNull(body);
            Assert.Equal("gemma3:4b", body.Model);
            Assert.False(body.Stream);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { status = "success" })
            };
        });

        OllamaClient client = CreateClient(handler);

        await client.PullModelAsync("gemma3:4b");
    }

    [Fact]
    public async Task DeleteModelAsync_ThrowsModelNotFoundForMissingModel()
    {
        StubHttpMessageHandler handler = new((request, cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = JsonContent.Create(new { error = "model 'missing' not found" })
            }));

        OllamaClient client = CreateClient(handler);

        OllamaApiException exception = await Assert.ThrowsAsync<OllamaApiException>(() => client.DeleteModelAsync("missing"));

        Assert.Equal(OllamaErrorKind.ModelNotFound, exception.Kind);
    }

    [Fact]
    public async Task ListModelsAsync_DoesNotExposeLargeExternalErrorBodies()
    {
        string tailMarker = "tail-marker-after-limit";
        StubHttpMessageHandler handler = new((request, cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(new string('x', 5000) + "\u0000" + tailMarker)
            }));

        OllamaClient client = CreateClient(handler);

        OllamaApiException exception = await Assert.ThrowsAsync<OllamaApiException>(() => client.ListModelsAsync());

        Assert.Equal(OllamaErrorKind.UnexpectedResponse, exception.Kind);
        Assert.DoesNotContain(tailMarker, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain('\u0000', exception.Message);
        Assert.DoesNotContain("xxxxx", exception.Message, StringComparison.Ordinal);
        Assert.Equal("Ollama ha restituito lo stato HTTP 500.", exception.Message);
    }

    [Fact]
    public async Task ListModelsAsync_ThrowsTimeoutWhenRequestIsCancelledInternally()
    {
        StubHttpMessageHandler handler = new((request, cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new OperationCanceledException()));

        OllamaClient client = CreateClient(
            handler,
            new OllamaSettings(
                OllamaEndpointOptions.DefaultBaseUrl,
                null,
                null,
                null,
                60,
                1));

        OllamaApiException exception = await Assert.ThrowsAsync<OllamaApiException>(() => client.ListModelsAsync());

        Assert.Equal(OllamaErrorKind.Timeout, exception.Kind);
    }

    [Fact]
    public async Task ChatSmokeAsync_AcceptsCompletedResponse()
    {
        StubHttpMessageHandler handler = new((request, cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { done = true })
            }));

        OllamaClient client = CreateClient(handler);

        await client.ChatSmokeAsync("gemma3:4b");
    }

    [Fact]
    public async Task GenerateChatAsync_SendsMessagesAndReturnsAssistantContent()
    {
        StubHttpMessageHandler handler = new(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("http://localhost:11434/api/chat", request.RequestUri?.ToString());

            JsonDocument body = await JsonDocument.ParseAsync(
                await request.Content!.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            Assert.Equal("gemma3:4b", body.RootElement.GetProperty("model").GetString());
            Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
            JsonElement messages = body.RootElement.GetProperty("messages");
            Assert.Equal("system", messages[0].GetProperty("role").GetString());
            Assert.Equal("regole", messages[0].GetProperty("content").GetString());
            Assert.Equal("domanda", messages[1].GetProperty("content").GetString());

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    done = true,
                    message = new
                    {
                        role = "assistant",
                        content = "risposta"
                    }
                })
            };
        });

        OllamaClient client = CreateClient(handler);

        string response = await client.GenerateChatAsync(
            "gemma3:4b",
            [new OllamaChatMessage("system", "regole"), new OllamaChatMessage("user", "domanda")]);

        Assert.Equal("risposta", response);
    }

    [Fact]
    public async Task GenerateChatAsync_SerializesConcurrentGenerationRequests()
    {
        TaskCompletionSource firstEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource secondEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        int activeRequests = 0;
        int maxActiveRequests = 0;
        StubHttpMessageHandler handler = new(async (request, cancellationToken) =>
        {
            int call = Interlocked.Increment(ref calls);
            int active = Interlocked.Increment(ref activeRequests);
            UpdateMaxActiveRequests(ref maxActiveRequests, active);

            if (call == 1)
            {
                firstEntered.SetResult();
                await releaseFirst.Task.WaitAsync(cancellationToken);
            }
            else
            {
                secondEntered.SetResult();
            }

            Interlocked.Decrement(ref activeRequests);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    done = true,
                    message = new
                    {
                        role = "assistant",
                        content = $"risposta {call}"
                    }
                })
            };
        });
        OllamaClient client = CreateClient(handler);

        Task<string> first = client.GenerateChatAsync(
            "gemma3:4b",
            [new OllamaChatMessage("user", "prima")]);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task<string> second = client.GenerateChatAsync(
            "gemma3:4b",
            [new OllamaChatMessage("user", "seconda")]);
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.False(secondEntered.Task.IsCompleted);

        releaseFirst.SetResult();
        await Task.WhenAll(first, second);

        Assert.True(secondEntered.Task.IsCompleted);
        Assert.Equal(1, maxActiveRequests);
    }

    private static void UpdateMaxActiveRequests(ref int maxActiveRequests, int active)
    {
        int current;
        do
        {
            current = Volatile.Read(ref maxActiveRequests);
            if (active <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref maxActiveRequests, active, current) != current);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_SendsChunkBatchAndReturnsVectors()
    {
        StubHttpMessageHandler handler = new(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("http://localhost:11434/api/embed", request.RequestUri?.ToString());

            JsonDocument body = await JsonDocument.ParseAsync(
                await request.Content!.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            Assert.Equal("nomic-embed-text:latest", body.RootElement.GetProperty("model").GetString());
            JsonElement input = body.RootElement.GetProperty("input");
            Assert.Equal(JsonValueKind.Array, input.ValueKind);
            Assert.Equal("chunk one", input[0].GetString());
            Assert.Equal("chunk two", input[1].GetString());
            Assert.False(body.RootElement.GetProperty("truncate").GetBoolean());
            Assert.False(body.RootElement.TryGetProperty("options", out _));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    embeddings = new[]
                    {
                        new[] { 1f, 0f, 0f },
                        new[] { 0f, 1f, 0f }
                    }
                })
            };
        });

        OllamaClient client = CreateClient(handler);

        IReadOnlyList<IReadOnlyList<float>> embeddings = await client.GenerateEmbeddingsAsync(
            "nomic-embed-text:latest",
            ["chunk one", "chunk two"]);

        Assert.Equal(2, embeddings.Count);
        Assert.Equal(3, embeddings[0].Count);
        Assert.Equal(1f, embeddings[0][0]);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_SendsManualNumCtxOnlyWhenConfigured()
    {
        StubHttpMessageHandler handler = new(async (request, cancellationToken) =>
        {
            JsonDocument body = await JsonDocument.ParseAsync(
                await request.Content!.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            Assert.False(body.RootElement.GetProperty("truncate").GetBoolean());
            Assert.Equal(4096, body.RootElement.GetProperty("options").GetProperty("num_ctx").GetInt32());

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { embeddings = new[] { new[] { 1f } } })
            };
        });

        OllamaClient client = CreateClient(handler);

        IReadOnlyList<IReadOnlyList<float>> embeddings = await client.GenerateEmbeddingsAsync(
            "nomic-embed-text:latest",
            ["chunk"],
            numCtx: 4096);

        Assert.Single(embeddings);
    }

    [Fact]
    public async Task GetVersionAndListRunningModels_ParseOllamaDiagnostics()
    {
        StubHttpMessageHandler handler = new((request, cancellationToken) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/version")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { version = "0.6.8" })
                });
            }

            Assert.Equal("/api/ps", request.RequestUri?.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    models = new[]
                    {
                        new
                        {
                            name = "gemma3:4b",
                            model = "gemma3:4b",
                            size = 3338801804L,
                            size_vram = 2147483648L,
                            digest = "sha256",
                            model_info = new Dictionary<string, object>
                            {
                                ["gemma3.context_length"] = 8192
                            }
                        }
                    }
                })
            });
        });

        OllamaClient client = CreateClient(handler);

        string? version = await client.GetVersionAsync();
        IReadOnlyList<OllamaRunningModelResponse> running = await client.ListRunningModelsAsync();

        Assert.Equal("0.6.8", version);
        Assert.Single(running);
        Assert.Equal("gemma3:4b", running[0].Name);
        Assert.Equal(8192, running[0].ContextLength);
        Assert.Equal(2147483648L, running[0].SizeVram);
    }

    private static OllamaClient CreateClient(
        HttpMessageHandler handler,
        OllamaSettings? settings = null)
    {
        return new OllamaClient(
            new HttpClient(handler),
            new StubOllamaSettingsService(settings ?? new OllamaSettings(
                OllamaEndpointOptions.DefaultBaseUrl,
                null,
                null,
                null,
                60,
                1)),
            new OllamaGenerationCoordinator());
    }

    private sealed record PullRequestBody(string Model, bool Stream);

    private sealed class StubOllamaSettingsService : IOllamaSettingsService
    {
        private readonly OllamaSettings settings;

        public StubOllamaSettingsService(OllamaSettings settings)
        {
            this.settings = settings;
        }

        public Task ClearMissingDefaultModelAsync(string modelName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<OllamaSettings> GetAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(settings);
        }

        public Task<OllamaSettings> UpdateAsync(OllamaSettings settings, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            this.handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return handler(request, cancellationToken);
        }
    }
}
